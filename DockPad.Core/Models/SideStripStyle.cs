namespace DockPad.Models;

/// <summary>
/// Rendu de la colonne latérale du Stream Dock ; Columns, la valeur 0, est le défaut.
/// </summary>
public enum SideStripStyle
{
    /// <summary>
    /// Barres verticales pour les quotas, le processeur et la mémoire.
    /// </summary>
    Columns,

    /// <summary>
    /// Fenêtre la plus consommée en grand, suivie de lignes compactes.
    /// </summary>
    HeroStack,

    /// <summary>
    /// Cadrans par fournisseur et pour le PC, avec un anneau par mesure.
    /// </summary>
    Rings,

    /// <summary>
    /// Colonne remplie au niveau de la fenêtre la plus consommée, complétée de pastilles.
    /// </summary>
    Reservoir,
}
