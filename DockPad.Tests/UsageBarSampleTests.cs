using DockPad.Models;
using DockPad.Services;
using DockPad.Services.StreamDock;
using DockPad.Services.Usage;

namespace DockPad.Tests;

/// <summary>
/// Les barres d'exemple des vignettes de style : valeurs fixes, aucune lecture de fournisseur ni mesure réelle, et
/// aucun geste ne touche la configuration enregistrée.
/// </summary>
public class UsageBarSampleTests
{
    [Fact]
    public void Colonnes_ValeursFixes()
    {
        using var bar = UsageBarSample.Create(SideStripStyle.Columns);

        Assert.Equal(BarState.Full, bar.State);
        Assert.Equal(["62%", "44%", "88%"], bar.Columns.Take(3).Select(column => column.Text));
        Assert.Equal(["23", "61"], bar.Columns.TakeLast(2).Select(column => column.NumberText));
    }

    [Theory]
    [InlineData(SideStripStyle.Columns)]
    [InlineData(SideStripStyle.HeroStack)]
    [InlineData(SideStripStyle.Rings)]
    [InlineData(SideStripStyle.Reservoir)]
    public void ChaqueStyle_ChargeEtStyleDemandes(SideStripStyle style)
    {
        using var bar = UsageBarSample.Create(style);

        Assert.Equal(style, bar.Style);
        Assert.Equal("23%", bar.Load?.CpuText);
        Assert.Equal("61%", bar.Load?.MemoryText);
        Assert.Equal(style == SideStripStyle.HeroStack, bar.ShowCurve);
    }

    [Fact]
    public void Heros_CourbeDouzePoints()
    {
        using var bar = UsageBarSample.Create(SideStripStyle.HeroStack);

        Assert.Equal(SystemLoadFeed.HistoryLength, bar.Load?.CpuSeries.Count);
    }

    [Fact]
    public void Gestes_NEcriventRienDansLaConfig()
    {
        var saves = 0;
        void OnChanged() => saves++;
        UsageConfigService.Changed += OnChanged;
        try
        {
            using var bar = UsageBarSample.Create(SideStripStyle.Columns);
            bar.Pick("codex");
            bar.Cycle(1);
        }
        finally
        {
            UsageConfigService.Changed -= OnChanged;
        }

        Assert.Equal(0, saves);
    }
}
