using System.Globalization;
using System.Threading;
using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Services;

/// <summary>
/// Quotas IA lus pour le serveur MCP (dockpad_usage_get) : session et semaine de chaque assistant
/// qui en expose un.
/// </summary>
public static class UsageActionService
{
    /// <summary>
    /// Le relais MCP attend la réponse sur son tube : au-delà, mieux vaut un refus qu'un appel figé.
    /// </summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Les fournisseurs du registre, donc les mêmes instances que le bandeau et le même créneau
    /// d'appel au quota Claude.
    /// </summary>
    public static UsageService Service { get; set; } = new();

    public static ActionResult GetQuotas(string? providerId) =>
        GetQuotas(providerId, Service, UsageConfigService.Load(), DateTime.Now);

    /// <summary>
    /// Sans fournisseur nommé : ceux du bandeau qui ont un quota à dire, démonstration exclue.
    /// Nommé : celui-là seul, même masqué, même sans quota.
    /// </summary>
    public static ActionResult GetQuotas(string? providerId, UsageService service, UsageConfig config, DateTime now)
    {
        using var timeout = new CancellationTokenSource(ReadTimeout);
        try
        {
            if (providerId == null)
            {
                var snapshots = service.RefreshAsync(config, timeout.Token).GetAwaiter().GetResult();
                var quotas = snapshots
                    .Where(usage => usage.IsDemo == false)
                    .Where(HasQuotaToReport)
                    .Select(usage => DescribeQuota(usage, now))
                    .ToList();
                return ActionResult.Success(new { providers = quotas });
            }

            var knownId = service.ProviderIds
                .Where(id => string.Equals(id, providerId, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (knownId == null)
                return ActionResult.Fail(
                    $"Fournisseur « {providerId} » inconnu. Fournisseurs connus : {string.Join(", ", service.ProviderIds)}.");

            var named = service.ReadOneAsync(knownId, timeout.Token).GetAwaiter().GetResult();
            if (named == null)
                return ActionResult.Fail($"Aucune donnée pour « {knownId} » : cet assistant n'est pas détecté sur ce poste.");

            return ActionResult.Success(new { providers = new[] { DescribeQuota(named, now) } });
        }
        catch (OperationCanceledException)
        {
            return ActionResult.Fail(
                $"La lecture des quotas a dépassé {ReadTimeout.TotalSeconds:0} s. Réessaie dans un instant.");
        }
    }

    /// <summary>
    /// Une jauge, ou la notice qui explique son absence. Gemini et Copilot n'ont ni l'une ni l'autre.
    /// </summary>
    private static bool HasQuotaToReport(AiUsage usage) =>
        usage.Session != null || usage.Week != null || usage.QuotaNotice.Length > 0;

    private static object DescribeQuota(AiUsage usage, DateTime now)
    {
        var quotaAvailable = usage.Session != null || usage.Week != null;
        var notice = usage.QuotaNotice.Length > 0 ? usage.QuotaNotice
            : quotaAvailable ? null
            : $"{usage.Name} n'expose pas de quota lisible par DockPad.";

        return new
        {
            id = usage.ProviderId,
            name = usage.Name,
            isDemo = usage.IsDemo,
            quotaAvailable,
            session = DescribeWindow(usage.Session, now),
            week = DescribeWindow(usage.Week, now),
            notice,
            noticeDetail = usage.QuotaNoticeNote.Length > 0 ? usage.QuotaNoticeNote : null,
        };
    }

    private static object? DescribeWindow(UsageWindow? window, DateTime now)
    {
        if (window == null) return null;

        int? minutesUntilReset = window.ResetsAt == null
            ? null
            : Math.Max(0, (int)Math.Ceiling((window.ResetsAt.Value - now).TotalMinutes));

        return new
        {
            usedPct = window.UsedPct,
            remainingPct = window.RemainingPct,
            resetsAt = ToIso(window.ResetsAt),
            minutesUntilReset,
            // Un relevé ancien n'est pas faux, mais il ne doit pas passer pour frais.
            stale = window.ObservedAt != null,
            observedAt = ToIso(window.ObservedAt),
        };
    }

    /// <summary>
    /// Heure locale avec son décalage : un modèle ne connaît pas le fuseau de la machine.
    /// </summary>
    private static string? ToIso(DateTime? local) =>
        local == null ? null : new DateTimeOffset(local.Value).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
}
