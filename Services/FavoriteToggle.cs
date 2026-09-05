using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Ce que décide le toggle étoile du popup de choix du navigateur : trouver, placer, nommer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le toggle agit immédiatement</b>, et c'est ce qui justifie sa forme : un bouton qui montre un
/// état doit dire la vérité. Il est déjà allumé à l'ouverture si l'URL est en favori, cocher ajoute,
/// décocher retire — et l'on peut mettre en favori sans jamais ouvrir le lien.
/// </para>
/// <para>
/// <b>Tout ce qui se décide est pur</b> : la fenêtre appelle et affiche. C'est ce qui rend le geste
/// vérifiable sans WPF et sans ouvrir de navigateur.
/// </para>
/// </remarks>
public static class FavoriteToggle
{
    /// <summary>Sur quelle page atterrit un favori, et faut-il la créer pour ça.</summary>
    /// <remarks>
    /// La <b>page</b>, et pas la case : celle-ci est choisie par <c>AddCore</c>, qui sait déjà le
    /// faire. Décider les deux ici, c'était deux implémentations du même balayage à garder
    /// d'accord — et une page créée pour rien le jour où la seconde refuse ce que la première a
    /// promis, par exemple parce qu'une requête MCP a pris la case entre les deux.
    /// </remarks>
    public readonly record struct FavoritePlacement(int Page, bool NeedsNewPage);

    /// <summary>
    /// Le favori qui vise cette URL, ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// La correspondance est <b>exacte</b>, et sur une tuile web uniquement. Conséquence assumée du
    /// choix « URL complète » : le toggle parle de <i>cette</i> page, pas du site. Un favori créé à
    /// la main qui pointerait la racine du site n'allume donc pas le toggle d'une page interne — ce
    /// qui est le comportement juste, les deux ne rouvrent pas la même chose.
    /// </remarks>
    public static ShortcutEntry? Find(List<ShortcutEntry> entries, string url) =>
        entries.FirstOrDefault(s => s.Type == ShortcutType.OpenUrl
                                    && string.Equals(s.Command, url, StringComparison.Ordinal));

    /// <summary>
    /// La première page qui a une case libre ; toutes pleines, une page est demandée.
    /// </summary>
    /// <remarks>
    /// <b>Règle distincte de celle d'<c>AddCore</c></b>, qui ne regarde que la page 0 et refuse si
    /// elle est pleine. <c>AddCore</c> n'est pas touché : sa sémantique tout-ou-rien est ce que le
    /// serveur MCP promet aux modèles, et la changer ici la changerait aussi pour les raccourcis.
    /// Le popup, lui, ne peut pas refuser — personne n'est devant l'écran pour lire le message.
    /// </remarks>
    public static FavoritePlacement Placement(List<ShortcutEntry> entries, List<PageConfig> configs)
    {
        int maxUsed = entries.Count > 0 ? entries.Max(s => s.Page) : -1;
        int maxConfig = configs.Count > 0 ? configs.Max(p => p.Index) : -1;
        int lastShown = Math.Max(Math.Max(maxUsed, maxConfig), 0);

        // Un balayage des cases, et non un comptage des entrées : deux tuiles peuvent partager une
        // position dans un fichier édité à la main, et une position peut être hors bornes. Compter
        // déclarerait alors une page pleine qui ne l'est pas.
        for (int page = 0; page <= lastShown; page++)
        {
            var occupied = entries.Where(s => s.Page == page).Select(s => (s.Row, s.Col)).ToHashSet();
            for (int row = 0; row < ShortcutActionService.GridRows; row++)
                for (int col = 0; col < ShortcutActionService.GridCols; col++)
                    if (!occupied.Contains((row, col)))
                        return new FavoritePlacement(page, NeedsNewPage: false);
        }

        return new FavoritePlacement(lastShown + 1, NeedsNewPage: true);
    }

    /// <summary>
    /// Nom de la tuile : le domaine, port compris s'il n'est pas celui du schéma.
    /// </summary>
    /// <remarks>
    /// <see cref="UrlRouterService.ExtractHost"/> et pas une seconde façon d'extraire un domaine :
    /// c'est déjà la fonction qui décide ce qu'est un hôte pour les règles de navigateur. Une URL
    /// qu'elle n'analyse pas garde son texte entier — un nom laid vaut mieux qu'une tuile sans nom,
    /// qui serait invisible dans la grille.
    /// </remarks>
    public static string NameFor(string url) => UrlRouterService.ExtractHost(url) ?? url;

    // ───────────── Enveloppe ─────────────

    /// <summary>
    /// Met l'URL en favori, ou l'en retire, et rend l'état obtenu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// L'ajout passe par <see cref="ShortcutActionService.AddAsync"/> et non par une écriture
    /// directe : c'est ce qui donne au favori l'icône du site, exactement comme une tuile web créée
    /// à la main, et qui garde le téléchargement <b>hors</b> du verrou global des configs.
    /// </para>
    /// <para>
    /// <b>Aucune fenêtre n'est supposée exister.</b> Au clic sur un lien, DockPad peut tourner en
    /// arrière-plan sans grille affichée : le toggle écrit dans le fichier, un point c'est tout.
    /// </para>
    /// </remarks>
    public static async Task<bool> SetAsync(string url, bool favorite, TileFiles? files = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Favorites);

        var existing = Find(ShortcutService.Load(files.EntriesPath), url);

        if (!favorite)
        {
            if (existing is not null)
                ShortcutActionService.Delete(existing.Page, existing.Row, existing.Col, files);
            return false;
        }

        if (existing is not null) return true;

        var placement = Placement(ShortcutService.Load(files.EntriesPath),
                                  PageConfigService.Load(files.PagesPath));

        // La page est créée d'abord : AddCore refuse une page qui n'existe pas encore, et cette
        // borne sur l'état initial est ce qui rend un lot tout-ou-rien vérifiable.
        if (placement.NeedsNewPage) PageActionService.Add(null, files);

        // Aucune position : AddCore prend la première case libre de la page, sous le verrou et sur
        // l'état du moment. Lui dicter une case calculée avant le verrou, c'est promettre à sa
        // place — et si une requête MCP l'a prise entretemps, l'ajout échoue alors qu'une autre
        // case était libre, en laissant derrière lui la page qu'on venait de créer pour rien.
        var result = await ShortcutActionService.AddAsync([new ShortcutAddItem
        {
            Page = placement.Page,
            Name = NameFor(url),
            Type = ShortcutType.OpenUrl,
            Command = url,
        }], files).ConfigureAwait(false);

        return result.Ok;
    }
}
