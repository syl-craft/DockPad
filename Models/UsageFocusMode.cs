namespace DockPad.Models;

/// <summary>
/// Comment le fournisseur mis en avant est choisi ; Manual, la valeur 0, est le défaut.
/// </summary>
public enum UsageFocusMode
{
    /// <summary>
    /// L'utilisateur choisit l'onglet.
    /// </summary>
    Manual,

    /// <summary>
    /// Le fournisseur le plus proche de sa limite est mis en avant.
    /// </summary>
    AutoClosestToLimit,
}
