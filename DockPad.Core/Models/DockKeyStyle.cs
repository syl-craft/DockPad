namespace DockPad.Models;

/// <summary>
/// Dessin des touches du Stream Dock ; Neon, la valeur 0, est le défaut.
/// </summary>
public enum DockKeyStyle
{
    /// <summary>
    /// Fond gris anthracite, icône en grand, nom blanc, trait de la couleur de l'icône en bas.
    /// </summary>
    Neon,

    /// <summary>
    /// Fond gris, icône en haut, nom en dessous.
    /// </summary>
    Classic,

    /// <summary>
    /// Couleur unie tirée de l'icône sur toute la touche.
    /// </summary>
    Flat,
}
