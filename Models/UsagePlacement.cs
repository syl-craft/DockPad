namespace DockPad.Models;

/// <summary>
/// Où s'affiche l'Usage IA dans la fenêtre d'accès rapide ; Bottom, la valeur 0, est le défaut.
/// </summary>
public enum UsagePlacement
{
    /// <summary>
    /// Bandeau sous la grille.
    /// </summary>
    Bottom,

    /// <summary>
    /// Barre verticale à droite de la grille.
    /// </summary>
    Right,
}
