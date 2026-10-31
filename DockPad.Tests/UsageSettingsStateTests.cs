using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Tests;

/// <summary>
/// Activation des contrôles de la fenêtre Usage IA selon le tableau de la spécification : la position et l'IA mise en
/// avant suivent « Usage IA activé », le style et la charge exigent en plus la position À droite, la courbe exige
/// encore la charge cochée.
/// </summary>
public class UsageSettingsStateTests
{
    private static UsageConfig Config(bool enabled = true, UsagePlacement placement = UsagePlacement.Right,
                                      bool showSystemLoad = true) =>
        new() { Enabled = enabled, Placement = placement, ShowSystemLoad = showSystemLoad };

    [Theory]
    [InlineData(UsagePlacement.Bottom)]
    [InlineData(UsagePlacement.Right)]
    public void Activation_UsageDesactive_ToutGrise(UsagePlacement placement)
    {
        var state = UsageSettingsState.From(Config(enabled: false, placement: placement));

        Assert.Equal(new UsageSettingsState(false, false, false, false, false), state);
    }

    [Fact]
    public void Activation_EnBas_StyleEtChargeGrises()
    {
        var state = UsageSettingsState.From(Config(placement: UsagePlacement.Bottom));

        Assert.True(state.CanChoosePosition);
        Assert.True(state.CanChooseFocus);
        Assert.False(state.CanChooseBarStyle);
        Assert.False(state.CanShowSystemLoad);
        Assert.False(state.CanShowLoadHistory);
    }

    [Fact]
    public void Activation_ChargeDecochee_CourbeGrisee()
    {
        var state = UsageSettingsState.From(Config(showSystemLoad: false));

        Assert.True(state.CanShowSystemLoad);
        Assert.False(state.CanShowLoadHistory);
    }

    [Fact]
    public void Activation_ADroite_ToutActif()
    {
        var state = UsageSettingsState.From(Config());

        Assert.Equal(new UsageSettingsState(true, true, true, true, true), state);
    }

    [Fact]
    public void Activation_ChargeDecochee_EnBas_CourbeGriseeAussi()
    {
        var state = UsageSettingsState.From(Config(placement: UsagePlacement.Bottom, showSystemLoad: false));

        Assert.False(state.CanShowLoadHistory);
    }
}
