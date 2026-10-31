using System.Windows.Input;
using DockPad.Models;
using DockPad.Views;

namespace DockPad.Tests;

/// <summary>
/// Décision de Ctrl + Tab : suivant, précédent ou rien. Hors des conditions de la spec, la touche garde son effet.
/// </summary>
public class UsageBarShortcutTests
{
    private static int? Handle(Key key = Key.Tab, ModifierKeys mods = ModifierKeys.Control, bool enabled = true,
                               UsagePlacement placement = UsagePlacement.Right, int eligibleCount = 3,
                               bool searchEmpty = true, bool moving = false, bool dragging = false) =>
        UsageBarShortcut.Handles(key, mods, enabled, placement, eligibleCount, searchEmpty, moving, dragging);

    [Fact]
    public void CtrlTab_Suivant_MajPrecedent()
    {
        Assert.Equal(1, Handle());
        Assert.Equal(-1, Handle(mods: ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void CtrlTab_EnBas_Rien()
    {
        Assert.Null(Handle(placement: UsagePlacement.Bottom));
    }

    [Fact]
    public void CtrlTab_UsageDesactive_Rien()
    {
        Assert.Null(Handle(enabled: false));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void CtrlTab_Recherche_Deplacement_Glisser_Rien(bool searchEmpty, bool moving, bool dragging)
    {
        // Seule la combinaison « recherche vide, rien en cours » agit : chaque autre ligne laisse la touche.
        var expectedActing = searchEmpty && moving == false && dragging == false;

        Assert.Equal(expectedActing ? 1 : (int?)null, Handle(searchEmpty: searchEmpty, moving: moving, dragging: dragging));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CtrlTab_UnSeulEligible_Rien(int eligibleCount)
    {
        Assert.Null(Handle(eligibleCount: eligibleCount));
    }

    [Fact]
    public void CtrlTab_DeuxEligibles_Agit()
    {
        Assert.Equal(1, Handle(eligibleCount: 2));
    }

    [Theory]
    [InlineData(Key.Tab, ModifierKeys.None)]
    [InlineData(Key.Tab, ModifierKeys.Shift)]
    [InlineData(Key.Tab, ModifierKeys.Alt)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.Left, ModifierKeys.Control)]
    [InlineData(Key.D1, ModifierKeys.Control)]
    public void Tab_Seul_Inchange(Key key, ModifierKeys mods)
    {
        Assert.Null(Handle(key, mods));
    }
}
