namespace DockPad.Models;

/// <summary>
/// Dessin des touches du Stream Dock ; Halo, la valeur 0, est le défaut.
/// </summary>
public enum DockKeyStyle
{
    /// <summary>
    /// Tuile arrondie teintée par l'icône, halo et reflet, nom en bas dans la tuile.
    /// </summary>
    Halo,

    /// <summary>
    /// Fond gris, icône en haut, nom en dessous.
    /// </summary>
    Classic,
}
