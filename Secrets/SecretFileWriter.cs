using System.IO;
using System.Text;
using System.Threading;

namespace DockPad.Secrets;

/// <summary>Un fichier de secret à produire : son nom, et sa valeur.</summary>
public sealed record SecretFile(string Name, string Value);

/// <summary>
/// Le <b>seul</b> fichier du dossier autorisé à écrire sur le disque.
/// </summary>
/// <remarks>
/// <para>
/// La garde « rien sur disque » du périmètre d'audit n'a pas été supprimée pour cette
/// fonctionnalité : elle a été <b>restreinte à ce fichier</b>. Partout ailleurs dans
/// <c>Secrets/</c>, écrire reste interdit et le test le prouve. La question « où un secret peut-il
/// toucher le disque ? » garde donc une réponse d'un seul mot.
/// </para>
/// <para>
/// <b>Aucun saut de ligne final, aucun BOM.</b> Vaultwarden rogne ce qu'il lit via <c>_FILE</c>,
/// mais <c>containerboot</c> lit <c>TS_AUTHKEY</c> par <c>file:</c> sans rien rogner : un
/// <c>\n</c> de trop y casse l'authentification Tailscale. La contrainte n'est pas cosmétique.
/// </para>
/// <para>
/// <b>Écriture en deux temps.</b> Tout est d'abord écrit à côté sous un nom temporaire, puis
/// basculé en place. Un jeu de secrets déjà correct n'est jamais détruit à moitié par une écriture
/// qui échoue au troisième fichier.
/// </para>
/// </remarks>
public static class SecretFileWriter
{
    /// <summary>Sous-dossier produit, à côté du fichier compose.</summary>
    public const string FolderName = "secrets";

    /// <summary>UTF-8 <b>sans</b> BOM : le BOM serait lu comme faisant partie du secret.</summary>
    private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Un nom de base peut-il devenir un fichier de ce dossier, et de ce dossier seul ?
    /// </summary>
    /// <remarks>
    /// <c>file: ./secrets/</c> donne un nom de base <b>vide</b> : le temporaire devenait alors
    /// <c>&lt;dossier&gt;\secrets.dockpad-tmp</c>, soit un secret en clair écrit <b>hors</b> du
    /// dossier — et hors du <c>.gitignore</c> que ce dossier pose pour lui-même. Un nom relatif
    /// (<c>..</c>) ou porteur d'un séparateur sortirait de la même façon.
    /// </remarks>
    public static bool IsWritableName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name != "." && name != ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.Contains('/') && !name.Contains('\\');

    /// <summary>
    /// Les noms de fichiers réclamés par deux secrets à la fois.
    /// </summary>
    /// <remarks>
    /// Le nom de base ne retient pas le dossier : deux <c>file:</c> de dossiers différents mais de
    /// même nom visaient le même fichier. Il en sortait <b>un</b> fichier portant la <b>mauvaise</b>
    /// valeur et l'autre absent — la garantie « rien ou tout » rompue en silence. On refuse avant
    /// d'écrire quoi que ce soit.
    /// </remarks>
    public static IReadOnlyList<string> Conflicts(IReadOnlyList<ComposeSecret> entries) =>
        entries
            .GroupBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => Loc.F("Inject_Error_DuplicateFile", g.Key))
            .ToList();

    /// <summary>
    /// Écrit les fichiers dans <c>&lt;dossier&gt;\secrets\</c> et rend leurs noms, dans l'ordre.
    /// </summary>
    /// <remarks>
    /// <b>Les temporaires sont effacés quoi qu'il arrive.</b> Ils portent des secrets en clair : une
    /// écriture qui lève à mi-parcours — nom illégal, disque plein, ACL — en laisserait derrière
    /// elle, sans nom dans aucun message et sans que rien ne les retire.
    /// </remarks>
    public static IReadOnlyList<string> Write(string folder, IReadOnlyList<SecretFile> files)
    {
        var target = Path.Combine(folder, FolderName);
        Directory.CreateDirectory(target);

        WriteGitIgnore(target);

        var staged = new List<(string Temp, string Final)>();

        try
        {
            foreach (var file in files)
            {
                var final = Path.Combine(target, file.Name);
                var temp = final + ".dockpad-tmp";
                File.WriteAllText(temp, file.Value, NoBom);
                staged.Add((temp, final));
            }

            // Toutes les destinations sont eprouvees AVANT d'en basculer une seule. Sans cela, un
            // verrou sur le troisieme fichier laissait les deux premiers portant les nouvelles
            // valeurs et le reste les anciennes — un jeu de secrets a moitie a jour, ce que cette
            // classe jure impossible. La sonde ne supprime pas la course, elle la ramene de
            // « toute la duree des basculements » a quelques microsecondes.
            foreach (var (_, final) in staged)
                EnsureWritable(final);

            foreach (var (temp, final) in staged)
                MoveWithRetry(temp, final);
        }
        finally
        {
            foreach (var (temp, _) in staged)
                Delete(temp);
        }

        return files.Select(f => f.Name).ToList();
    }

    /// <summary>
    /// Bascule un temporaire en place, en retentant tant qu'un verrou <b>passager</b> s'y oppose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un antivirus qui vient de scanner le fichier, un indexeur, un client de synchronisation :
    /// tous tiennent brièvement une destination fraîchement écrite. Sans reprise, cette poignée de
    /// millisecondes devient un échec dur — et le dossier <c>secrets/</c> vit justement dans un
    /// dépôt, souvent synchronisé.
    /// </para>
    /// <para>
    /// C'est le remède que MSBuild applique à la même opération : il retente dix fois avant de
    /// renoncer. Ici, deux secondes suffisent — au-delà, ce n'est plus un verrou passager mais un
    /// fichier réellement occupé, et il faut le dire plutôt que d'attendre.
    /// </para>
    /// </remarks>
    private static void MoveWithRetry(string temp, string final)
    {
        const int attempts = 20;

        for (var i = 1; ; i++)
        {
            try
            {
                File.Move(temp, final, overwrite: true);
                return;
            }
            catch (IOException ex) when (i < attempts && IsTransientLock(ex)) { Thread.Sleep(100); }
        }
    }

    /// <summary>
    /// Une destination existante est-elle réellement remplaçable, maintenant ?
    /// </summary>
    /// <remarks>
    /// On l'ouvre en écriture sans partage, et on referme aussitôt. C'est ce que fait la bascule
    /// juste après ; l'échouer ici coûte un fichier intact plutôt qu'un jeu incohérent.
    /// </remarks>
    private static void EnsureWritable(string final)
    {
        if (!File.Exists(final)) return;

        for (var i = 1; ; i++)
        {
            try
            {
                using var probe = new FileStream(final, FileMode.Open, FileAccess.Write, FileShare.None);
                return;
            }
            catch (IOException ex) when (i < 20 && IsTransientLock(ex)) { Thread.Sleep(100); }
        }
    }

    /// <summary>
    /// Le verrou est-il de ceux qui se relâchent, ou de ceux qui ne se relâcheront pas ?
    /// </summary>
    /// <remarks>
    /// Ne retenter que sur un <b>partage refusé</b>. Un fichier absent, un dossier disparu, un
    /// attribut lecture seule ou une ACL qui refuse échouent identiquement à chaque tentative :
    /// y insister brûle deux secondes par fichier — dix pour le compose de référence — avant de
    /// dire ce qu'on savait dès la première.
    /// </remarks>
    private static bool IsTransientLock(IOException ex)
    {
        const int sharingViolation = 0x20;
        const int lockViolation = 0x21;

        if (ex is FileNotFoundException or DirectoryNotFoundException) return false;

        var code = ex.HResult & 0xFFFF;
        return code is sharingViolation or lockViolation;
    }

    /// <summary>Efface un temporaire, sans jamais masquer l'échec qui a mené jusqu'ici.</summary>
    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Services.LogService.Warn(ex, "Nettoyage d'un fichier temporaire de secret"); }
    }

    /// <summary>
    /// Fait que le dossier s'ignore lui-même.
    /// </summary>
    /// <remarks>
    /// Les fichiers produits n'ont <b>pas d'extension</b> — <c>ts-authkey</c>, <c>smtp-password</c>.
    /// Une règle <c>*.key</c> dans le <c>.gitignore</c> d'un dépôt ne les couvre pas, et ils
    /// partiraient au premier <c>git add .</c>. Un <c>.gitignore</c> local règle le cas sans
    /// invoquer git ni deviner ses règles, et vaut aussi hors dépôt.
    /// </remarks>
    private static void WriteGitIgnore(string target)
    {
        var path = Path.Combine(target, ".gitignore");
        if (File.Exists(path)) return;

        File.WriteAllText(path, "# Secrets en clair — jamais versionnés.\n*\n!.gitignore\n", NoBom);
    }
}
