using DockPad.Models;

namespace DockPad.Views;

/// <summary>
/// Calculs purs de la barre à droite : largeur que la fenêtre gagne ou perd, et moment où la charge du PC se mesure.
/// </summary>
public static class UsageBarLayout
{
    /// <summary>
    /// Largeur que la fenêtre gagne quand la barre apparaît : la barre et l'écart qui la sépare de la grille, moins une
    /// marge extérieure. La grille et la barre restant centrées ensemble, chacune des deux marges extérieures perd
    /// ainsi la moitié de sa largeur.
    /// </summary>
    public static double AddedWidth(double barWidth, double gap, double outerMargin) => barWidth + gap - outerMargin;

    /// <summary>
    /// Largeur et largeur minimale de la fenêtre après un changement de position. Le retour en bas retire exactement
    /// ce que l'arrivée à droite a ajouté, sans passer sous la nouvelle largeur minimale.
    /// </summary>
    public static (double Width, double MinWidth) Apply(UsagePlacement from, UsagePlacement to, double width,
        double minWidth, double added)
    {
        if (from == to) return (width, minWidth);
        if (to == UsagePlacement.Right) return (width + added, minWidth + added);

        var newMinWidth = minWidth - added;
        return (Math.Max(width - added, newMinWidth), newMinWidth);
    }

    /// <summary>
    /// La charge du PC se mesure seulement quand la fenêtre est sous les yeux, Usage IA activé, la barre à droite et la
    /// charge cochée.
    /// </summary>
    public static bool ShouldSampleLoad(bool windowActive, bool enabled, UsagePlacement placement, bool showSystemLoad) =>
        windowActive && enabled && placement == UsagePlacement.Right && showSystemLoad;
}
