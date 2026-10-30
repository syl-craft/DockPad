using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Quels contrôles de la fenêtre Usage IA sont actifs pour une configuration donnée, sans WPF.
/// </summary>
public readonly record struct UsageSettingsState(
    bool CanChoosePosition,
    bool CanChooseBarStyle,
    bool CanChooseFocus,
    bool CanShowSystemLoad,
    bool CanShowLoadHistory)
{
    public static UsageSettingsState From(UsageConfig config)
    {
        var isBarShown = config.Enabled && config.Placement == UsagePlacement.Right;

        return new UsageSettingsState(
            CanChoosePosition: config.Enabled,
            CanChooseBarStyle: isBarShown,
            CanChooseFocus: config.Enabled,
            CanShowSystemLoad: isBarShown,
            CanShowLoadHistory: isBarShown && config.ShowSystemLoad);
    }
}
