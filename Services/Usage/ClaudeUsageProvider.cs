using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Consommation de Claude Code : jetons lus dans les transcripts locaux, quota officiel lu sur
/// l'endpoint <c>oauth/usage</c> quand il répond.
/// </summary>
/// <remarks>
/// Le dossier de départ est injectable et retombe sur <c>%USERPROFILE%</c> : c'est ce qui rend la
/// détection et le scan testables sur un dossier temporaire, sans toucher au profil réel.
/// </remarks>
public sealed class ClaudeUsageProvider : IUsageProvider
{
    private readonly string _home;
    private readonly ClaudeLimitsClient _limits;
    private readonly Func<DateTime> _clock;

    /// <summary>Vrai dès que l'indisponibilité du quota a été signalée dans le journal.</summary>
    private static bool _limitsFailureLogged;

    public string Id => "claude";
    public string Name => "Claude Code";

    public ClaudeUsageProvider(string? home = null, HttpClient? http = null, Func<DateTime>? clock = null)
    {
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _limits = new ClaudeLimitsClient(http);
        _clock = clock ?? (() => DateTime.Now);
    }

    public AiProbe Probe()
    {
        try
        {
            var root = ClaudeUsageReader.ScanRoots(_home).FirstOrDefault(Directory.Exists);
            if (root is null)
            {
                return new AiProbe { Available = false, DisplayName = Name, Detail = "non installé" };
            }

            var count = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).Take(1).Count();
            return new AiProbe
            {
                Available = true,
                DisplayName = Name,
                DataPath = root,
                Detail = count == 0 ? "installé, aucune donnée de session" : "",
            };
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Détection de Claude Code");
            return new AiProbe { Available = false, DisplayName = Name, Detail = "détection impossible" };
        }
    }

    public async Task<AiUsage?> ReadAsync(CancellationToken ct)
    {
        var now = _clock();

        // Task.Run : la lecture parcourt des centaines de fichiers (mesuré 2 s sur un profil réel).
        // Sans elle, tout ce travail s'exécute sur le thread d'interface avant le premier await,
        // et la fenêtre gèle à chaque affichage puis à chaque rafraîchissement.
        var totals = await Task.Run(() => ReadTotals(now), ct).ConfigureAwait(false);
        if (totals is null) return null;

        var limits = await ReadLimitsAsync(ct).ConfigureAwait(false);

        return new AiUsage
        {
            ProviderId = Id,
            Name = Name,
            Glyph = "✳",
            AccentColor = "#D97757",
            UsageUrl = "https://claude.ai/new#settings/usage",
            Model = totals.Model,
            Cost = ClaudePricing.Format(totals.Cost),
            SessionTokens = totals.Session,
            DayTokens = totals.Day,
            MonthTokens = totals.Month,
            Requests = totals.Requests,
            Session = limits?.Session,
            Week = limits?.Week,
        };
    }

    private UsageAggregator.UsageTotals? ReadTotals(DateTime now)
    {
        try
        {
            var entries = ClaudeUsageReader.Read(_home, UsageWindows.ScanStart(now));
            return entries.Count == 0 ? null : ClaudeUsageReader.Aggregate(entries, now);
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Lecture de la consommation Claude Code");
            return null;
        }
    }

    private async Task<ClaudeLimitsClient.ClaudeLimits?> ReadLimitsAsync(CancellationToken ct)
    {
        try
        {
            var path = ClaudeLimitsClient.CredentialsPath(_home);
            if (!File.Exists(path)) return null;

            // Le jeton reste local à cette méthode : ni champ, ni journal, ni config.
            var token = ClaudeLimitsClient.ReadAccessToken(File.ReadAllText(path), DateTime.UtcNow);
            if (token is null) return null;

            var limits = await _limits.FetchAsync(token, ct).ConfigureAwait(false);
            if (limits is null) NoteQuotaUnavailable("réponse inexploitable");
            return limits;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Le type de l'exception, jamais son message : celui-ci pourrait embarquer le chemin du
            // fichier de credentials. Sans cette notice, une exception ici était totalement
            // silencieuse — le journal ne permettait pas de distinguer « quota obtenu » de
            // « quota en échec », alors que c'est la première question qu'on se pose.
            NoteQuotaUnavailable(ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Signale l'indisponibilité du quota <b>une seule fois par session</b> : l'endpoint n'est pas
    /// documenté, et un journal à chaque rafraîchissement noierait les vraies anomalies.
    /// </summary>
    private static void NoteQuotaUnavailable(string cause)
    {
        if (_limitsFailureLogged) return;
        _limitsFailureLogged = true;
        LogService.Info($"Quota Claude indisponible ({cause}) — jauges masquées, "
                      + "métriques de jetons conservées");
    }
}
