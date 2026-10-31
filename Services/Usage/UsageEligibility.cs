using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Quels fournisseurs la barre peut mettre en avant, et lequel est le plus proche de sa limite.
/// Seule définition de l'éligibilité : la barre, le cycle et le mode Auto passent tous par ici.
/// </summary>
public static class UsageEligibility
{
    /// <summary>
    /// Fournisseur réel qui a une fenêtre de quota, ou une notice qui explique son absence.
    /// </summary>
    public static bool IsEligible(AiUsage usage)
    {
        if (usage.IsDemo) return false;

        return usage.Session != null || usage.Week != null || usage.QuotaNotice.Length > 0;
    }

    /// <summary>
    /// Le fournisseur éligible dont une fenêtre non ancienne est la plus consommée, ou <c>null</c>
    /// s'il n'y en a aucune. À égalité, le premier dans l'ordre du bandeau l'emporte.
    /// </summary>
    public static string? ClosestToLimit(IReadOnlyList<AiUsage> usages)
    {
        string? closestId = null;
        var highestUsedPct = -1;

        foreach (var usage in usages.Where(IsEligible))
        {
            var freshUsedPcts = new[] { usage.Session, usage.Week }
                .OfType<UsageWindow>()
                .Where(window => window.ObservedAt == null)
                .Select(window => window.UsedPct)
                .ToList();
            if (freshUsedPcts.Count == 0) continue;

            var usedPct = freshUsedPcts.Max();
            if (usedPct <= highestUsedPct) continue;   // strict : l'ordre du bandeau départage

            closestId = usage.ProviderId;
            highestUsedPct = usedPct;
        }

        return closestId;
    }
}
