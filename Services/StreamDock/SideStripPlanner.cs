using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Une fenêtre de quota telle que la colonne l'affiche ; IsStale l'emporte sur IsAlert au rendu.
/// </summary>
public sealed record QuotaGauge(string ProviderId, string ProviderName, string WindowLabel, int UsedPct,
    bool IsAlert, bool IsStale, DateTime? ResetsAt, UsageWindowKind WindowKind);

public enum UsageWindowKind { Session, Week }

/// <summary>
/// Quotas de la plus consommée à la moins consommée ; null pour une mesure pas encore disponible.
/// </summary>
public sealed record SideStripContent(IReadOnlyList<QuotaGauge> Quotas, int? CpuPct, int? MemoryPct);

public static class SideStripPlanner
{
    /// <summary>
    /// Entrée dans l'ordre du bandeau, masqués exclus ; sans démo ni fournisseur sans quota, tri stable par consommation.
    /// Bandeau désactivé : aucun quota, la charge du PC reste.
    /// </summary>
    public static SideStripContent Plan(IReadOnlyList<AiUsage> usages, UsageConfig config, SystemLoad load)
    {
        if (config.Enabled == false)
            return new SideStripContent([], load.CpuPct, load.MemoryPct);

        var quotas = usages
            .Where(usage => usage.IsDemo == false && (usage.Session != null || usage.Week != null))
            .SelectMany(usage => Windows(usage)
                .Select(window => CreateGauge(usage, window.Window, window.Kind, window.DefaultLabel, config.AlertThreshold)))
            .OrderByDescending(quota => quota.UsedPct)
            .ToArray();

        return new SideStripContent(quotas, load.CpuPct, load.MemoryPct);
    }

    private static IEnumerable<(UsageWindow Window, UsageWindowKind Kind, string DefaultLabel)> Windows(AiUsage usage)
    {
        if (usage.Session != null)
            yield return (usage.Session, UsageWindowKind.Session, Loc.T("StreamDock_Session"));
        if (usage.Week != null)
            yield return (usage.Week, UsageWindowKind.Week, Loc.T("StreamDock_Week"));
    }

    private static QuotaGauge CreateGauge(AiUsage usage, UsageWindow window, UsageWindowKind kind, string defaultLabel, int threshold)
    {
        var label = string.IsNullOrEmpty(window.Label) ? defaultLabel : window.Label;
        return new QuotaGauge(usage.ProviderId, usage.Name, label, window.UsedPct,
            IsAlert: UsageFormat.IsCritical(window.UsedPct, threshold),
            IsStale: window.ObservedAt != null, ResetsAt: window.ResetsAt, WindowKind: kind);
    }
}
