using System.IO;

namespace DockPad.Secrets;

/// <summary>
/// Ce qu'une résolution d'annotations a produit : les fichiers à écrire, ce qui manquait, et les
/// fichiers devenus périmés.
/// </summary>
/// <param name="Stale">
/// Les noms de fichiers dont la clé a <b>disparu du coffre</b>. Ce ne sont pas encore des fichiers :
/// c'est l'appelant qui regarde lesquels existent réellement sur le disque, parce que ce type est
/// pur et ne connaît pas de dossier.
/// </param>
/// <param name="ItemCount">
/// Items de coffre <b>distincts</b> réellement lus — pas le nombre de fichiers. Les cinq secrets du
/// compose de référence viennent tous du même item.
/// </param>
public sealed record SecretBundleResult(
    IReadOnlyList<SecretFile> Files,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Stale,
    int ItemCount)
{
    /// <summary>Tout ce qui était demandé a été résolu : le seul cas qui a droit au vert.</summary>
    public bool Complete => Missing.Count == 0;
}

/// <summary>
/// Où un <c>template:</c> a le droit de pointer.
/// </summary>
/// <remarks>
/// <para>
/// <b>C'est une surface de lecture, et elle vient d'un fichier.</b> Le chemin est écrit dans le
/// compose : sans garde, un <c>template: ../../../.ssh/id_rsa</c> ferait lire une clé privée et
/// l'écrirait, rendue, dans <c>secrets/</c>. C'est la seule annotation qui désigne <i>quoi lire</i>,
/// donc la seule qui demande cette vérification.
/// </para>
/// <para>
/// <b>On compare les chemins RÉSOLUS, jamais la chaîne.</b> Chercher <c>..</c> dedans se contourne —
/// séparateurs mélangés, chemin court 8.3, jonction de répertoire. Résoudre puis vérifier que le
/// résultat est bien sous la racine ne se contourne pas de la même façon.
/// </para>
/// </remarks>
public static class SecretTemplatePath
{
    /// <summary>Le chemin absolu du modèle, ou <c>null</c> s'il sort du dossier du compose.</summary>
    public static string? Resolve(string composeFolder, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;

        // Un chemin enracine — absolu, UNC, ou « /etc/passwd » — n'est pas relatif au compose.
        if (Path.IsPathRooted(relative)) return null;

        string root, full;
        try
        {
            root = Path.GetFullPath(composeFolder);
            full = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception)
        {
            // Caracteres illegaux, chemin trop long : ce n'est pas un modele atteignable.
            return null;
        }

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

/// <summary>
/// Résout les annotations d'un compose en fichiers de secrets — <b>au mieux</b>, et en disant ce
/// qui manque.
/// </summary>
/// <remarks>
/// <para>
/// Entièrement pur : le coffre entre par une fonction, les modèles entrent <b>déjà lus</b>, aucun
/// accès au disque, aucun WPF. C'est ce qui permet de vérifier la règle qui compte — <b>une clé
/// absente n'écrit pas son fichier, les autres si</b> — sans jamais lancer la CLI Bitwarden ni
/// toucher un dossier.
/// </para>
/// <para>
/// <b>Deux natures d'annotation, un seul fichier produit.</b> <c>item</c>+<c>field</c> : la valeur
/// du coffre <i>est</i> le contenu. <c>template</c> : un modèle local est rendu. Les deux sont
/// exclusifs, <see cref="ComposeSecrets"/> refuse avant d'arriver ici.
/// </para>
/// <para>
/// <b>Un fichier n'est jamais écrit vide, ni à moitié rendu.</b> C'est la moitié dangereuse du
/// rendu partiel : Vaultwarden rogne ce qu'il lit via <c>_FILE</c>, mais <c>containerboot</c> lit
/// <c>TS_AUTHKEY</c> par <c>file:</c> sans rien roger — il partirait avec une chaîne vide et
/// échouerait plus tard, loin d'ici. Ne pas écrire est bruyant ; écrire du vide est silencieux.
/// </para>
/// </remarks>
public static class SecretBundle
{
    /// <param name="templates">
    /// Contenu des modèles, par chemin relatif tel qu'écrit dans l'annotation. Lus par l'appelant :
    /// ce type reste pur, et le chemin a été validé par <see cref="SecretTemplatePath"/> avant.
    /// </param>
    public static SecretBundleResult Resolve(
        IReadOnlyList<ComposeSecret> entries,
        Func<SecretMarker, SecretLookup> lookup,
        IReadOnlyDictionary<string, string>? templates = null)
    {
        var files = new List<SecretFile>();
        var missing = new List<string>();
        var stale = new List<string>();
        var read = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Template is { } path)
            {
                ResolveTemplate(entry, path, lookup, templates, files, missing, stale, read);
                continue;
            }

            var marker = entry.Marker!.Value;
            var found = lookup(marker);

            if (string.IsNullOrEmpty(found.Value))
            {
                missing.Add(found.Failure
                    ?? Loc.F("Inject_Error_EmptyField", marker.Item, marker.Field));
                stale.Add(entry.FileName);
                continue;
            }

            files.Add(new SecretFile(entry.FileName, found.Value));
            read.Add(marker.Item);
        }

        return new SecretBundleResult(
            files,
            missing.Distinct(StringComparer.Ordinal).ToList(),
            stale.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            read.Count);
    }

    private static void ResolveTemplate(
        ComposeSecret entry, string path,
        Func<SecretMarker, SecretLookup> lookup,
        IReadOnlyDictionary<string, string>? templates,
        List<SecretFile> files, List<string> missing, List<string> stale, HashSet<string> read)
    {
        if (templates is null || !templates.TryGetValue(path, out var content))
        {
            // L'appelant refuse normalement AVANT d'ouvrir le coffre. Ce filet existe pour que ce
            // type reste correct seul, sans dependre de la discipline de son appelant.
            missing.Add(Loc.F("Inject_Error_TemplateMissing", entry.Key, path));
            stale.Add(entry.FileName);
            return;
        }

        var (text, unresolved) = SecretTemplate.RenderStrict(content, lookup);

        if (text is null)
        {
            missing.AddRange(unresolved);
            stale.Add(entry.FileName);
            return;
        }

        foreach (var marker in SecretTemplate.FindMarkers(content))
            read.Add(marker.Item);

        files.Add(new SecretFile(entry.FileName, NormalizeNewlines(text)));
    }

    /// <summary>
    /// Fins de ligne en LF.
    /// </summary>
    /// <remarks>
    /// La destination est un conteneur Linux, et le modèle vient d'un dépôt git qui peut l'avoir
    /// extrait en CRLF sous Windows. Ne s'applique qu'aux modèles : une <b>valeur</b> du coffre est
    /// un secret, on l'écrit telle qu'elle est et on n'y touche pas.
    /// </remarks>
    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");
}
