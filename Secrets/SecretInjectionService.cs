using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services;

namespace DockPad.Secrets;

/// <summary>Ce que le mode fichiers a produit : les noms écrits, où, et combien d'items lus.</summary>
/// <param name="ItemCount">
/// Items de coffre <b>distincts</b> consultés, et non le nombre de fichiers : les cinq secrets du
/// compose de référence viennent tous de <c>vaultwarden-infra</c>, et annoncer « 5 items lus »
/// donnerait une fausse idée de ce qui a été interrogé.
/// </param>
/// <param name="Stale">
/// Fichiers <b>présents sur le disque</b> dont la clé a disparu du coffre. Ils ne sont pas touchés :
/// la suppression demande un geste, sinon un coffre temporairement inaccessible détruirait un
/// déploiement qui marchait.
/// </param>
public sealed record SecretFilesOutcome(
    string Folder, IReadOnlyList<string> Written, int ItemCount, IReadOnlyList<string> Stale);

/// <summary>
/// Ce qu'une étape a produit : un rendu, des fichiers, <b>ou les deux</b> — ou une liste d'échecs.
/// </summary>
/// <remarks>
/// <b><see cref="Missing"/> n'est pas <see cref="Failures"/>.</b> Une clé que le coffre ne connaît
/// pas est une donnée : on produit ce qu'on peut et on la nomme. <see cref="Failures"/> reste ce
/// qui empêche de produire quoi que ce soit — CLI absente, déverrouillage refusé, fichier
/// illisible. Confondre les deux, c'est soit bloquer sur un détail, soit taire une panne.
/// </remarks>
/// <remarks>
/// <c>Diagnostic</c> est l'erreur standard de la CLI — <b>jamais</b> sa sortie standard, qui porte
/// les données du coffre. Il n'est pas traduit : c'est un diagnostic, il va au journal et en
/// infobulle derrière une phrase, elle, traduite. Même règle que les causes d'indisponibilité du
/// quota dans le bandeau Usage.
/// </remarks>
public sealed record InjectionReport
{
    /// <summary>Ce qui a échoué. Vide <b>si et seulement si</b> quelque chose a été produit.</summary>
    public IReadOnlyList<string> Failures { get; private init; } = [];

    /// <summary>Le rendu, en mode presse-papier.</summary>
    public SecretRenderResult? Render { get; private init; }

    /// <summary>Les fichiers écrits, en mode fichiers.</summary>
    public SecretFilesOutcome? Files { get; private init; }

    /// <summary>Ce que le coffre n'a pas su rendre. Nommé, parce que ça vient du fichier source.</summary>
    public IReadOnlyList<string> Missing { get; private init; } = [];

    public string? Diagnostic { get; private init; }

    public bool Ok => Failures.Count == 0;

    /// <summary>Produit, et sans trou : le seul cas qui a droit au vert.</summary>
    public bool Complete => Ok && Missing.Count == 0;

    public static InjectionReport Fail(string failure, string? diagnostic = null) =>
        new() { Failures = [failure], Diagnostic = diagnostic };

    public static InjectionReport Failed(IReadOnlyList<string> failures, string? diagnostic = null) =>
        new() { Failures = failures, Diagnostic = diagnostic };

    /// <summary>Le rendu presse-papier — un échec de rendu devient un échec de rapport, ici et nulle part ailleurs.</summary>
    public static InjectionReport Rendered(SecretRenderResult result) =>
        result.Ok
            ? new() { Render = result, Missing = result.Missing }
            : new() { Failures = result.Failures };

    /// <summary>Ce qu'une injection a produit : l'un, l'autre, ou les deux, avec ses manques.</summary>
    public static InjectionReport Produced(
        SecretRenderResult? render, SecretFilesOutcome? files, IReadOnlyList<string> missing) =>
        new() { Render = render, Files = files, Missing = missing };

    /// <summary>Le cache local a été rafraîchi. Rien n'a été lu, rien n'a été produit.</summary>
    public static InjectionReport Synced() => new() { DidSync = true };

    /// <summary>Vrai quand l'opération était une synchronisation, et qu'elle a abouti.</summary>
    public bool DidSync { get; private init; }
}

/// <summary>
/// Enchaîne les appels à la CLI, du fichier au texte rendu.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune clé de session n'est conservée.</b> Elle naît d'un <c>unlock</c>, vit le temps de
/// <see cref="RenderAsync"/>, et sort de portée avec la méthode. C'est le choix retenu : DockPad
/// démarre avec Windows et tourne des semaines, une clé de coffre n'a rien à y faire — au prix du
/// mot de passe maître à chaque injection.
/// </para>
/// <para>
/// <b>Quatre appels au plus</b>, dont un seul <c>list items</c> qui ramène tout. Le script d'origine
/// lançait une recherche par item ; ramener l'ensemble en un appel est plus rapide, et déplace la
/// résolution du côté testable de la frontière (voir <see cref="SecretVault"/>).
/// </para>
/// </remarks>
public static class SecretInjectionService
{
    /// <summary>
    /// D'où viennent les valeurs. Un seul champ, et c'est <b>tout</b> ce que l'orchestrateur sait
    /// du coffre.
    /// </summary>
    /// <remarks>
    /// <b>En lecture seule, et sans point d'injection.</b> Une seconde source n'existe pas encore :
    /// un sélecteur ou un passeur de dépendance seraient de l'API spéculative, et le jour venu le
    /// choix se fera sur le <b>préfixe du marqueur</b> — pour que le fichier dise d'où viennent ses
    /// secrets — et non sur un réglage global. Ce qui compte ici est la frontière, pas sa
    /// configurabilité.
    /// </remarks>
    private static readonly ISecretSource Source = new BitwardenSecretSource();

    // ───────────── Décisions pures ─────────────

    /// <summary>
    /// Faut-il renoncer avant même de demander le mot de passe maître ?
    /// </summary>
    /// <remarks>
    /// Un seul statut est distingué, et c'est celui qui appelle une <b>autre action</b> de
    /// l'utilisateur : sans <c>bw login</c>, aucun mot de passe ne servirait à rien. Tout le reste —
    /// y compris un statut illisible parce que la CLI a changé de format — mène au déverrouillage,
    /// parce que sans clé de session la CLI ne peut rien lire de toute façon.
    /// </remarks>
    public static string? StatusFailure(string? status) =>
        status == "unauthenticated" ? Loc.T("Inject_Error_NotLoggedIn") : null;

    /// <summary>
    /// L'identifiant de l'organisation configurée, par nom ou par identifiant.
    /// </summary>
    /// <remarks>
    /// L'échec <b>liste les organisations disponibles</b> : sans elles, on ne sait pas si on s'est
    /// trompé de nom ou si l'organisation n'a jamais été créée, et ce sont deux corrections
    /// différentes.
    /// </remarks>
    public static (string? Id, string? Failure) ResolveOrganisation(
        IReadOnlyList<BwOrganization> organisations, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return (null, null);

        var match = organisations.FirstOrDefault(
            o => string.Equals(o.Name, configured, StringComparison.OrdinalIgnoreCase)
              || string.Equals(o.Id, configured, StringComparison.OrdinalIgnoreCase));

        if (match is not null) return (match.Id, null);

        var available = organisations.Count == 0
            ? Loc.T("Inject_Error_OrgNone")
            : string.Join(", ", organisations.Select(o => o.Name));

        return (null, Loc.F("Inject_Error_OrgMissing", configured, available));
    }

    // ───────────── Le fichier ─────────────

    /// <summary>
    /// Au-delà, on refuse sans lire.
    /// </summary>
    /// <remarks>
    /// L'entrée de menu vit sur <b>tous</b> les fichiers : un clic droit sur une image disque
    /// chargerait des giga-octets en mémoire et les passerait au parseur YAML. Un gabarit de
    /// déploiement pèse quelques dizaines de kilo-octets ; quatre méga-octets laissent large.
    /// </remarks>
    public const int MaxTemplateBytes = 4 * 1024 * 1024;

    /// <summary>Le contenu du gabarit, ou l'échec à afficher. Le fichier n'est jamais modifié.</summary>
    public static (string? Content, InjectionReport? Failure) ReadTemplate(string path)
    {
        if (!File.Exists(path))
            return (null, InjectionReport.Fail(Loc.F("Inject_Error_FileMissing", Path.GetFileName(path))));

        var size = new FileInfo(path).Length;
        if (size > MaxTemplateBytes)
            return (null, InjectionReport.Fail(
                Loc.F("Inject_Error_FileTooBig", MaxTemplateBytes / (1024 * 1024))));

        try { return (File.ReadAllText(path), null); }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Lecture du gabarit à injecter");
            return (null, InjectionReport.Fail(Loc.T("Inject_Error_FileUnreadable"), ex.GetType().Name));
        }
    }

    // ───────────── Les appels, tous derrière la frontière ─────────────

    /// <summary>Peut-on demander sa clé à l'utilisateur ? <c>null</c> = oui.</summary>
    public static async Task<InjectionReport?> PreflightAsync(CancellationToken token)
    {
        var failure = await Source.PreflightAsync(token).ConfigureAwait(false);

        return failure is null ? null : InjectionReport.Fail(failure.Message, failure.Diagnostic);
    }

    /// <summary>
    /// Date de la dernière mise à jour de la vue locale, ou <c>null</c> si la source n'en a pas.
    /// </summary>
    /// <remarks>
    /// Aucun mot de passe n'est requis : la fenêtre des Options affiche l'âge du cache sans jamais
    /// approcher un secret.
    /// </remarks>
    public static Task<DateTime?> LastSyncAsync(CancellationToken token) =>
        Source.LastRefreshAsync(token);

    /// <summary>Rafraîchit la vue locale à la demande, depuis les Options.</summary>
    public static async Task<InjectionReport> SyncAsync(string masterPassword, CancellationToken token)
    {
        var failure = await Source.RefreshAsync(masterPassword, token).ConfigureAwait(false);

        return failure is null
            ? InjectionReport.Synced()
            : InjectionReport.Fail(failure.Message, failure.Diagnostic);
    }


    /// <summary>
    /// Déverrouille une fois, puis produit ce que le fichier demande : le rendu, les fichiers, ou
    /// les deux.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Un seul déverrouillage et un seul <c>list items</c> pour les deux sorties.</b> Enchaîner
    /// les deux méthodes d'avant aurait payé deux fois le prix du coffre pour le même mot de passe.
    /// </para>
    /// <para>
    /// <b>Ce qui bloque est vérifié avant d'ouvrir le coffre</b> : annotations illisibles, deux
    /// annotations visant le même fichier, nom qui sortirait du dossier. Aucune de ces trois ne
    /// produira rien de bon — inutile de réclamer un mot de passe maître pour s'en apercevoir après.
    /// </para>
    /// <para>
    /// <b>Ne rien avoir produit du tout est un échec</b>, pas un succès vide : c'est le seul garde
    /// qui reste de la règle du tout-ou-rien, et c'est celui qui compte.
    /// </para>
    /// </remarks>
    public static async Task<InjectionReport> InjectAsync(
        string content, string folder, SecretMode mode, string masterPassword,
        bool syncFirst, CancellationToken token)
    {
        IReadOnlyList<ComposeSecret> entries = [];
        var templates = new Dictionary<string, string>(StringComparer.Ordinal);

        if (mode is SecretMode.Files or SecretMode.Both)
        {
            var (scanned, annotationFailures, yamlError) = ComposeSecrets.Extract(content);
            if (annotationFailures.Count > 0)
                return InjectionReport.Failed(annotationFailures, yamlError);

            var blocking = SecretFileWriter.Conflicts(scanned)
                .Concat(scanned.Where(e => !SecretFileWriter.IsWritableName(e.FileName))
                               .Select(e => Loc.F("Inject_Error_BadFileName", e.Key)))
                .Concat(ReadTemplates(scanned, folder, templates))
                .ToList();

            if (blocking.Count > 0) return InjectionReport.Failed(blocking);
            entries = scanned;
        }

        var opening = await Source.OpenAsync(masterPassword, syncFirst, token).ConfigureAwait(false);
        if (opening.Failure is { } refused)
            return InjectionReport.Fail(refused.Message, refused.Diagnostic);

        // La SEULE chose que la source rend : de quoi resoudre un marqueur. Tout ce qui sait
        // comment le coffre s'appelle, s'authentifie et se lit reste derriere cette fonction.
        var lookup = opening.Lookup!;
        var warning = opening.Warning;

        var missing = new List<string>();

        // Une synchro qui echoue n'annule PAS l'injection : le cache local reste lisible, et
        // refuser de travailler parce que le reseau est coupe serait pire. Mais elle ne peut pas
        // se taire — travailler sur des valeurs peut-etre datees SANS le dire est exactement le
        // piege que cette option existe pour fermer.
        if (warning is not null) missing.Add(warning);
        SecretFilesOutcome? files = null;
        SecretRenderResult? render = null;

        if (entries.Count > 0)
        {
            var bundle = SecretBundle.Resolve(entries, lookup, templates);
            missing.AddRange(bundle.Missing);

            var written = SecretFileWriter.Write(folder, bundle.Files);
            var target = Path.Combine(folder, SecretFileWriter.FolderName);

            // Les perimes sont ceux qui EXISTENT vraiment : proposer la suppression d'un fichier
            // absent ferait douter de ce que la fenetre sait du disque.
            var stale = SecretFileWriter.Existing(folder, bundle.Stale);

            files = new SecretFilesOutcome(target, written, bundle.ItemCount, stale);
        }

        if (mode is SecretMode.Clipboard or SecretMode.Both)
        {
            render = SecretTemplate.Render(content, lookup);

            if (render.Ok) missing.AddRange(render.Missing);
            else
            {
                // En mode presse-papier seul, un rendu qui n'aboutit pas est TOUT l'echec. Combine
                // aux fichiers, c'est une moitie manquante : les fichiers ecrits restent acquis, et
                // la raison rejoint la liste des manques plutot que d'effacer ce qui a marche.
                if (files is null) return InjectionReport.Failed(render.Failures);

                missing.AddRange(render.Failures);
                render = null;
            }
        }

        if (render is null && files is null)
            return InjectionReport.Failed(Fallback(missing));

        return InjectionReport.Produced(render, files, missing.Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Lit les modèles désignés par <c>template:</c>, et rend ce qui empêche de continuer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Avant d'ouvrir le coffre.</b> Un modèle introuvable ou hors du dossier ne produira rien
    /// de bon — inutile de réclamer un mot de passe maître pour s'en apercevoir après.
    /// </para>
    /// <para>
    /// <b>Le chemin est vérifié par <see cref="SecretTemplatePath"/>, jamais pris tel quel.</b>
    /// C'est la seule annotation qui désigne <i>quoi lire</i> : sans cette garde, un compose pourrait
    /// faire lire n'importe quel fichier de la machine et l'écrire, rendu, dans <c>secrets/</c>.
    /// </para>
    /// </remarks>
    private static List<string> ReadTemplates(
        IReadOnlyList<ComposeSecret> entries, string folder, Dictionary<string, string> templates)
    {
        var failures = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.Template is not { } relative || templates.ContainsKey(relative)) continue;

            var full = SecretTemplatePath.Resolve(folder, relative);
            if (full is null)
            {
                failures.Add(Loc.F("Inject_Error_TemplateOutside", entry.Key, relative));
                continue;
            }

            if (!File.Exists(full))
            {
                failures.Add(Loc.F("Inject_Error_TemplateMissing", entry.Key, relative));
                continue;
            }

            // Meme plafond que le gabarit principal : l'annotation pourrait viser une image disque.
            if (new FileInfo(full).Length > MaxTemplateBytes)
            {
                failures.Add(Loc.F("Inject_Error_TemplateTooBig", entry.Key, relative));
                continue;
            }

            try { templates[relative] = File.ReadAllText(full); }
            catch (Exception ex)
            {
                LogService.Warn(ex, "Lecture d'un modèle de secret");
                failures.Add(Loc.F("Inject_Error_TemplateUnreadable", entry.Key, relative));
            }
        }

        return failures;
    }

    /// <summary>Un échec doit dire ce qui a échoué — même quand la liste des manques est vide.</summary>
    private static IReadOnlyList<string> Fallback(List<string> missing) =>
        missing.Count > 0 ? missing.Distinct(StringComparer.Ordinal).ToList()
                          : [Loc.T("Inject_Error_NoMarkers")];

}
