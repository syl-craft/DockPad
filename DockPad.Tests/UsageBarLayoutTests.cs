using DockPad.Models;
using DockPad.Views;

namespace DockPad.Tests;

/// <summary>
/// Largeur de la fenêtre quand la barre apparaît ou disparaît : la largeur ajoutée se retire exactement,
/// une taille choisie par l'utilisateur entre les deux est conservée, et la largeur minimale suit.
/// </summary>
public class UsageBarLayoutTests
{
    private const double BarWidth = 90;
    private const double TileGap = 10;
    private static readonly double Added = UsageBarLayout.AddedWidth(BarWidth, TileGap);

    [Fact]
    public void LargeurAjoutee_EstLaBarrePlusLEcartEntreTuiles()
    {
        Assert.Equal(100, Added);
    }

    [Fact]
    public void LargeurBarre_AllerRetour_RendLaLargeur()
    {
        var right = UsageBarLayout.Apply(UsagePlacement.Bottom, UsagePlacement.Right, 850, 850, Added);
        var back = UsageBarLayout.Apply(UsagePlacement.Right, UsagePlacement.Bottom, right.Width, right.MinWidth, Added);

        Assert.Equal(950, right.Width);
        Assert.Equal(850, back.Width);
        Assert.Equal(850, back.MinWidth);
    }

    [Fact]
    public void LargeurBarre_RedimensionneEntreDeux_RetireLAjout()
    {
        var right = UsageBarLayout.Apply(UsagePlacement.Bottom, UsagePlacement.Right, 850, 850, Added);
        var resized = 1000d;

        var back = UsageBarLayout.Apply(UsagePlacement.Right, UsagePlacement.Bottom, resized, right.MinWidth, Added);

        Assert.Equal(1000 - Added, back.Width);
    }

    [Fact]
    public void LargeurBarre_RetourEnBas_BorneeParLaLargeurMinimale()
    {
        var back = UsageBarLayout.Apply(UsagePlacement.Right, UsagePlacement.Bottom, 950, 950, Added);

        Assert.Equal(850, back.Width);
        Assert.Equal(850, back.MinWidth);
    }

    [Fact]
    public void LargeurBarre_MinWidthSuit()
    {
        var right = UsageBarLayout.Apply(UsagePlacement.Bottom, UsagePlacement.Right, 850, 850, Added);

        Assert.Equal(950, right.MinWidth);
    }

    [Fact]
    public void LargeurBarre_MemePosition_RienNeChange()
    {
        var same = UsageBarLayout.Apply(UsagePlacement.Right, UsagePlacement.Right, 950, 950, Added);

        Assert.Equal((950d, 950d), same);
    }

    [Theory]
    [InlineData(true, true, UsagePlacement.Right, true, true)]
    [InlineData(false, true, UsagePlacement.Right, true, false)]
    [InlineData(true, false, UsagePlacement.Right, true, false)]
    [InlineData(true, true, UsagePlacement.Bottom, true, false)]
    [InlineData(true, true, UsagePlacement.Right, false, false)]
    public void ChargeDuPc_MesureSeulementBarreVisibleEtCochee(bool windowActive, bool enabled,
        UsagePlacement placement, bool showSystemLoad, bool expected)
    {
        Assert.Equal(expected, UsageBarLayout.ShouldSampleLoad(windowActive, enabled, placement, showSystemLoad));
    }
}
