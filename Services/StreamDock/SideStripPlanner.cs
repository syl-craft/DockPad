using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Une fenêtre de quota telle que la colonne l'affiche ; IsStale l'emporte sur IsAlert au rendu.
/// ResetIn est null si la remise à zéro est inconnue ou déjà passée.
/// </summary>
public sealed record QuotaGauge(string ProviderId, string ProviderName, string WindowLabel, int UsedPct,
    bool IsAlert, bool IsStale, DateTime? ResetsAt, UsageWindowKind WindowKind, TimeSpan? ResetIn = null);

public enum UsageWindowKind { Session, Week }

/// <summary>
/// Quotas de l'IA principale d'abord, puis de la plus consommée à la moins consommée ; null pour une mesure
/// pas encore disponible.
/// </summary>
public sealed record SideStripContent(IReadOnlyList<QuotaGauge> Quotas, int? CpuPct, int? MemoryPct);

public static class SideStripPlanner
{
    /// <summary>
    /// Entrée dans l'ordre du bandeau, masqués exclus ; sans démo ni fournisseur sans quota, tri stable par consommation
    /// après les fenêtres de <paramref name="principalProviderId"/>, l'IA choisie dans le bandeau.
    /// Bandeau désactivé : aucun quota, la charge du PC reste. Sans <paramref name="now"/>, aucun temps restant.
    /// </summary>
    public static SideStripContent Plan(IReadOnlyList<AiUsage> usages, UsageConfig config, SystemLoad load,
        DateTime? now = null, string? principalProviderId = null)
    {
        if (config.Enabled == false)
            return new SideStripContent([], load.CpuPct, load.MemoryPct);

        // Même repli que le bandeau tant qu'il n'a rien affiché : son fournisseur par défaut.
        var principal = string.IsNullOrEmpty(principalProviderId) ? config.DefaultProviderId : principalProviderId;
        var quotas = usages
            .Where(usage => usage.IsDemo == false && (usage.Session != null || usage.Week != null))
            .SelectMany(usage => Windows(usage)
                .Select(window => CreateGauge(usage, window.Window, window.Kind, window.DefaultLabel, config.AlertThreshold, now)))
            .OrderByDescending(quota => string.Equals(quota.ProviderId, principal, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(quota => quota.UsedPct)
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

    private static QuotaGauge CreateGauge(AiUsage usage, UsageWindow window, UsageWindowKind kind, string defaultLabel,
        int threshold, DateTime? now)
    {
        var label = string.IsNullOrEmpty(window.Label) ? defaultLabel : window.Label;
        TimeSpan? resetIn = now != null && window.ResetsAt != null && window.ResetsAt > now
            ? window.ResetsAt - now
            : null;
        return new QuotaGauge(usage.ProviderId, usage.Name, label, window.UsedPct,
            IsAlert: UsageFormat.IsCritical(window.UsedPct, threshold),
            IsStale: window.ObservedAt != null, ResetsAt: window.ResetsAt, WindowKind: kind, ResetIn: resetIn);
    }
}
