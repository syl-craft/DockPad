using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Consommation de Codex : jetons lus dans les rollouts locaux.
/// </summary>
/// <remarks>
/// Lit jetons et quotas dans les rollouts locaux. Masque les quotas périmés avec une notice.
/// </remarks>
public sealed class CodexUsageProvider : IUsageProvider
{
    private readonly string _home;
    private readonly Func<DateTime> _clock;

    public string Id => "codex";
    public string Name => "Codex";

    /// <summary>
    /// Identité visuelle, déclarée une seule fois : la sonde et l'instantané la lisent ici.
    /// </summary>
    private const string PastilleGlyph = "C";
    private const string PastilleAccent = "#10A37F";

    public CodexUsageProvider(string? home = null, Func<DateTime>? clock = null)
    {
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _clock = clock ?? (() => DateTime.Now);
    }

    public AiProbe Probe()
    {
        try
        {
            var root = CodexUsageReader.ScanRoots(_home).FirstOrDefault(Directory.Exists);
            if (root is null)
            {
                return new AiProbe
                {
                    Available = false,
                    DisplayName = Name,
                    Glyph = PastilleGlyph,
                    AccentColor = PastilleAccent,
                    Detail = Loc.T("Probe_NotInstalled"),
                };
            }

            var hasRollout = CodexUsageReader.ScanRoots(_home)
                .Where(Directory.Exists)
                .Any(r => Directory.EnumerateFiles(r, "rollout-*.jsonl", SearchOption.AllDirectories).Any());

            return new AiProbe
            {
                Available = true,
                DisplayName = Name,
                Glyph = PastilleGlyph,
                AccentColor = PastilleAccent,
                DataPath = root,
                Detail = hasRollout ? "" : Loc.T("Probe_NoSessionData"),
            };
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Détection de Codex");
            return new AiProbe
            {
                Available = false,
                DisplayName = Name,
                Glyph = PastilleGlyph,
                AccentColor = PastilleAccent,
                Detail = Loc.T("Probe_DetectionFailed"),
            };
        }
    }

    public async Task<AiUsage?> ReadAsync(CancellationToken ct)
    {
        var now = _clock();

        var snapshot = await Task.Run(() =>
        {
            try
            {
                return CodexUsageReader.ReadSnapshot(_home, UsageWindows.ScanStart(now), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogService.Warn(ex, "Lecture de la consommation Codex");
                return new CodexUsageReader.Snapshot([], null);
            }
        }, ct).ConfigureAwait(false);

        // Détecté mais inactif sur la période : un instantané à zéro plutôt que rien, pour que le
        // fournisseur garde son onglet. Absent du bandeau veut dire « pas installé ».
        if (snapshot.Entries.Count == 0 && snapshot.Quota is null && !Probe().Available) return null;
        var totals = snapshot.Entries.Count == 0 ? UsageAggregator.Empty : UsageAggregator.Aggregate(snapshot.Entries, now);
        var quota = CodexQuotaReader.Current(snapshot.Quota, now);
        var hasQuota = quota?.Session is not null || quota?.Week is not null;

        return new AiUsage
        {
            ProviderId = Id,
            Name = Name,
            Glyph = PastilleGlyph,
            AccentColor = PastilleAccent,
            Model = totals.Model,
            SessionTokens = totals.Session,
            DayTokens = totals.Day,
            MonthTokens = totals.Month,
            Requests = totals.Requests,
            UsageUrl = "https://chatgpt.com/codex/settings/usage",
            Session = quota?.Session,
            Week = quota?.Week,
            QuotaNotice = hasQuota ? "" : Loc.T("Usage_Codex_QuotaNotice"),
            QuotaNoticeNote = hasQuota ? "" : snapshot.Quota is { } last
                ? Loc.F("Usage_Codex_QuotaLastSeen", last.ObservedAt.LocalDateTime)
                : Loc.T("Usage_Codex_QuotaMissing"),
        };
    }
}
