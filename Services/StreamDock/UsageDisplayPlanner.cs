using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Services.StreamDock;

public abstract record UsageDisplay
{
    public sealed record Gauge(string ProviderId, string Label, int UsedPct, string Color, bool IsStale) : UsageDisplay;
    public sealed record Identity(string ProviderId, string Name, DateTime? NextReset) : UsageDisplay;
}

public static class UsageDisplayPlanner
{
    /// <summary>
    /// Relevés dans l'ordre du bandeau, déjà filtrés des masqués ; null par écran signifie noir.
    /// </summary>
    public static IReadOnlyList<UsageDisplay?> Plan(IReadOnlyList<AiUsage> usages, UsageConfig config, int displayCount)
    {
        var displays = new UsageDisplay?[displayCount];
        if (config.Enabled == false || displayCount == 0)
            return displays;

        var providers = usages
            .Where(usage => usage.IsDemo == false && (usage.Session != null || usage.Week != null))
            .ToList();
        if (providers.Count == 0)
            return displays;

        IEnumerable<UsageDisplay> planned;
        if (providers.Count == 1)
        {
            var usage = providers[0];
            var windows = Windows(usage).ToList();
            var nextReset = windows.Select(window => window.Window.ResetsAt).Min();
            planned = windows
                .Select(window => (UsageDisplay)CreateGauge(usage, window.Window, window.DefaultLabel, config.AlertThreshold))
                .Append(new UsageDisplay.Identity(usage.ProviderId, usage.Name, nextReset));
        }
        else
        {
            planned = providers
                .Select(usage =>
                {
                    var selected = Windows(usage).OrderByDescending(window => window.Window.UsedPct).First();
                    return CreateGauge(usage, selected.Window, selected.DefaultLabel, config.AlertThreshold);
                })
                .OrderByDescending(gauge => gauge.UsedPct)
                .Take(3);
        }

        var display = 0;
        foreach (var content in planned.Take(displayCount))
            displays[display++] = content;

        return displays;
    }

    private static IEnumerable<(UsageWindow Window, string DefaultLabel)> Windows(AiUsage usage)
    {
        if (usage.Session != null)
            yield return (usage.Session, Loc.T("StreamDock_Session"));
        if (usage.Week != null)
            yield return (usage.Week, Loc.T("StreamDock_Week"));
    }

    private static UsageDisplay.Gauge CreateGauge(AiUsage usage, UsageWindow window, string defaultLabel, int threshold)
    {
        var label = string.IsNullOrEmpty(window.Label) ? defaultLabel : window.Label;
        return new UsageDisplay.Gauge(usage.ProviderId, label, window.UsedPct,
            UsageFormat.GaugeColor(window.UsedPct, threshold), IsStale: window.ObservedAt != null);
    }
}
