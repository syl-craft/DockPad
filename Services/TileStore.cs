using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Le seul endroit qui sait quel fichier du profil porte quelle grille de tuiles.
/// </summary>
/// <remarks>
/// <para>
/// La grille sait déjà tout faire — pages, positions, déplacements, dépôts. Ce qu'elle ne savait
/// pas, c'est <b>de quel fichier</b> elle vient : <c>ShortcutService.Load()</c> et
/// <c>PageConfigService.Load()</c> étaient appelés en dur à une trentaine d'endroits. Poser la
/// question ici, une fois, est toute la feature des favoris.
/// </para>
/// <para>
/// <b>Le format des deux cibles est identique</b>, volontairement : <c>favorites.json</c> est une
/// <c>List&lt;ShortcutEntry&gt;</c>, relisible par la même sérialisation, sauvegardable par le même
/// mécanisme et éditable à la main comme <c>shortcuts.json</c>. Un second modèle aurait dupliqué le
/// socle entier pour ne rien apporter.
/// </para>
/// </remarks>
/// <summary>Les deux fichiers d'une grille, passés ensemble aux services d'actions.</summary>
/// <remarks>
/// Les enveloppes prennent cette paire plutôt qu'une <see cref="TileTarget"/> pour la même raison
/// que <c>UsageConfigService.Load(path)</c> existe à côté de <c>Load()</c> : sans un point où poser
/// des chemins choisis, le routage de cible ne se vérifierait que sur le profil réel de la
/// machine — donc pas du tout.
/// </remarks>
public sealed record TileFiles(string EntriesPath, string PagesPath);

public static class TileStore
{
    /// <summary>Les fichiers d'une cible. C'est le pont entre le mode affiché et le disque.</summary>
    public static TileFiles FilesFor(TileTarget target) => new(EntriesPath(target), PagesPath(target));

    /// <summary>Fichier des tuiles d'une grille.</summary>
    public static string EntriesPath(TileTarget target) => AppPaths.File(
        target == TileTarget.Favorites ? "favorites.json" : "shortcuts.json");

    /// <summary>Fichier des pages d'une grille (icônes des boutons de pagination).</summary>
    public static string PagesPath(TileTarget target) => AppPaths.File(
        target == TileTarget.Favorites ? "favorite-pages.json" : "pages.json");

    /// <summary>Les quatre fichiers des deux grilles, pour la sauvegarde de configuration.</summary>
    public static IEnumerable<string> AllPaths()
    {
        foreach (var target in new[] { TileTarget.Shortcuts, TileTarget.Favorites })
        {
            yield return EntriesPath(target);
            yield return PagesPath(target);
        }
    }

    /// <summary>
    /// Cible nommée par le serveur MCP. Absente ou vide = les raccourcis.
    /// </summary>
    /// <remarks>
    /// <b>Une valeur inconnue lève</b>, elle ne retombe pas sur les raccourcis : écrire dans la
    /// mauvaise grille sans le dire est le pire des deux comportements. Le dispatcher transforme
    /// l'exception en refus nommé, que le modèle appelant peut lire et corriger.
    /// </remarks>
    public static TileTarget Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TileTarget.Shortcuts;

        return value.Trim().ToLowerInvariant() switch
        {
            "shortcuts" => TileTarget.Shortcuts,
            "favorites" => TileTarget.Favorites,
            // Le jet tient sur une ligne : la garde des littéraux français exclut les messages
            // d'exception en cherchant « throw new » sur la ligne du texte.
            _ => throw new ArgumentException($"target « {value} » inconnue : \"shortcuts\" ou \"favorites\"."),
        };
    }
}
