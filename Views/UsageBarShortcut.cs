using System.Windows.Input;
using DockPad.Models;

namespace DockPad.Views;

/// <summary>
/// Ctrl + Tab change l'IA mise en avant, dans les seules conditions où il ne vole rien à la fenêtre.
/// </summary>
public static class UsageBarShortcut
{
    /// <summary>
    /// +1 pour Ctrl + Tab, −1 pour Ctrl + Maj + Tab, null quand la touche garde son comportement : Usage IA désactivé,
    /// position en bas, moins de deux fournisseurs éligibles, recherche en cours, tuile en déplacement ou en glissement.
    /// </summary>
    public static int? Handles(Key key, ModifierKeys mods, bool enabled, UsagePlacement placement, int eligibleCount,
        bool searchEmpty, bool moving, bool dragging)
    {
        if (key != Key.Tab) return null;
        if (enabled == false || placement != UsagePlacement.Right) return null;
        if (eligibleCount < 2 || searchEmpty == false || moving || dragging) return null;

        return mods switch
        {
            ModifierKeys.Control => 1,
            ModifierKeys.Control | ModifierKeys.Shift => -1,
            _ => null,
        };
    }
}
