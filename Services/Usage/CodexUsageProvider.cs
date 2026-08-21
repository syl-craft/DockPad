using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Consommation de Codex : jetons lus dans les rollouts locaux.
/// </summary>
/// <remarks>
/// <b>Ni quota, ni coût.</b> Codex expose bien ses pourcentages de limite, mais seulement en lançant
/// <c>codex app-server --stdio</c> et en dialoguant en JSON-RPC : un processus enfant à chaque
/// rafraîchissement, toutes les minutes, pour deux nombres. Hors de proportion tant que rien ne le
/// réclame. Et je n'ai pas de tarif public fiable à appliquer, donc pas de coût affiché.
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
                    Detail = "non installé",
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
                Detail = hasRollout ? "" : "installé, aucune donnée de session",
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
                Detail = "détection impossible",
            };
        }
    }

    public async Task<AiUsage?> ReadAsync(CancellationToken ct)
    {
        var now = _clock();

        var totals = await Task.Run(() =>
        {
            try
            {
                var entries = CodexUsageReader.Read(_home, UsageWindows.ScanStart(now));
                return entries.Count == 0 ? null : UsageAggregator.Aggregate(entries, now);
            }
            catch (Exception ex)
            {
                LogService.Warn(ex, "Lecture de la consommation Codex");
                return null;
            }
        }, ct).ConfigureAwait(false);

        if (totals is null) return null;

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
        };
    }
}
