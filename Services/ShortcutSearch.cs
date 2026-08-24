using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Filtrage de la barre de recherche : quelles tuiles répondent à une requête, et dans quel ordre.
/// </summary>
/// <remarks>
/// Sortie du gestionnaire <c>TextChanged</c>, où la règle était mêlée au chargement des icônes et à
/// l'ouverture du popup. La vue garde ce qui la regarde — ouvrir le popup, charger les images ; la
/// règle vit ici et se teste.
/// </remarks>
public static class ShortcutSearch
{
    /// <summary>
    /// Tuiles dont le nom contient la requête, par ordre alphabétique. Requête vide : aucun
    /// résultat — la barre vide ne doit pas ouvrir un popup de toutes les tuiles.
    /// </summary>
    /// <remarks>
    /// Les cases vides portent un nom vide et sont écartées explicitement : toute chaîne contient la
    /// chaîne vide, elles remonteraient donc sur chaque recherche.
    /// </remarks>
    public static List<ShortcutEntry> Filter(IEnumerable<ShortcutEntry> entries, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return [];

        return entries
            .Where(e => e.Name.Length > 0
                        && e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
