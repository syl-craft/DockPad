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
/// Résout les annotations d'un compose en fichiers de secrets — <b>au mieux</b>, et en disant ce
/// qui manque.
/// </summary>
/// <remarks>
/// <para>
/// Entièrement pur : le coffre entre par une fonction, aucun accès au disque, aucun WPF. C'est ce
/// qui permet de vérifier la règle qui compte — <b>une clé absente n'écrit pas son fichier, les
/// autres si</b> — sans jamais lancer la CLI Bitwarden ni toucher un dossier.
/// </para>
/// <para>
/// <b>Un fichier n'est jamais écrit vide.</b> C'est la moitié dangereuse du rendu partiel :
/// Vaultwarden rogne ce qu'il lit via <c>_FILE</c>, mais <c>containerboot</c> lit
/// <c>TS_AUTHKEY</c> par <c>file:</c> sans rien roger — il partirait avec une chaîne vide et
/// échouerait plus tard, loin d'ici. Ne pas écrire est bruyant ; écrire du vide est silencieux.
/// </para>
/// <para>
/// <b>Un fichier existant dont la clé a disparu n'est pas touché non plus.</b> Le supprimer d'office
/// ferait d'un coffre temporairement inaccessible la cause d'un déploiement détruit. Il est
/// seulement <i>signalé</i>, et la suppression demande un geste — voir
/// <see cref="SecretFileWriter.Delete"/>.
/// </para>
/// </remarks>
public static class SecretBundle
{
    public static SecretBundleResult Resolve(
        IReadOnlyList<ComposeSecret> entries, Func<SecretMarker, SecretLookup> lookup)
    {
        var files = new List<SecretFile>();
        var missing = new List<string>();
        var stale = new List<string>();
        var read = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var found = lookup(entry.Marker);

            if (string.IsNullOrEmpty(found.Value))
            {
                missing.Add(found.Failure
                    ?? Loc.F("Inject_Error_EmptyField", entry.Marker.Item, entry.Marker.Field));
                stale.Add(entry.FileName);
                continue;
            }

            files.Add(new SecretFile(entry.FileName, found.Value));
            read.Add(entry.Marker.Item);
        }

        return new SecretBundleResult(
            files,
            missing.Distinct(StringComparer.Ordinal).ToList(),
            stale.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            read.Count);
    }
}
