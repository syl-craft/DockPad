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

        // La case revient a ShortcutActionService : c'est lui qui connait la grille, et deux
        // balayages a garder d'accord auraient fini par diverger.
        var slot = ShortcutActionService.FirstFreeSlot(ShortcutService.Load(files.EntriesPath),
                                                       PageConfigService.Load(files.PagesPath));

        // La page est créée d'abord : AddCore refuse une page qui n'existe pas encore, et cette
        // borne sur l'état initial est ce qui rend un lot tout-ou-rien vérifiable.
        if (slot.NeedsNewPage) PageActionService.Add(null, files);

        // Aucune position : AddCore prend la première case libre de la page, sous le verrou et sur
        // l'état du moment. Lui dicter une case calculée avant le verrou, c'est promettre à sa
        // place — et si une requête MCP l'a prise entretemps, l'ajout échoue alors qu'une autre
        // case était libre, en laissant derrière lui la page qu'on venait de créer pour rien.
        var result = await ShortcutActionService.AddAsync([new ShortcutAddItem
        {
            Page = slot.Page,
            Name = NameFor(url),
            Type = ShortcutType.OpenUrl,
            Command = url,
        }], files).ConfigureAwait(false);

        return result.Ok;
    }
}
