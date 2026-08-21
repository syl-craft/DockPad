using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Consommation de Gemini CLI : jetons lus dans les sessions locales.
/// </summary>
/// <remarks>
/// <b>Ni quota, ni coût.</b> Gemini n'expose pas de pourcentage de limite lisible localement, donc
/// les deux jauges restent masquées. Et je n'ai pas de tarif public fiable à appliquer : afficher un
/// montant inventé serait pire que d'afficher un tiret.
/// </remarks>
public sealed class GeminiUsageProvider : IUsageProvider
{
    private readonly string _home;
    private readonly Func<DateTime> _clock;

    public string Id => "gemini";
    public string Name => "Gemini CLI";

    public GeminiUsageProvider(string? home = null, Func<DateTime>? clock = null)
    {
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _clock = clock ?? (() => DateTime.Now);
    }

    public AiProbe Probe()
    {
        try
        {
            var root = GeminiUsageReader.ScanRoot(_home);
            if (!Directory.Exists(root))
            {
                return new AiProbe { Available = false, DisplayName = Name, Detail = "non installé" };
            }

            var hasSession = Directory.EnumerateDirectories(root, "chats", SearchOption.AllDirectories)
                .SelectMany(d => Directory.EnumerateFiles(d, "session-*.*"))
                .Any();

            return new AiProbe
            {
                Available = true,
                DisplayName = Name,
                DataPath = root,
                Detail = hasSession ? "" : "installé, aucune donnée de session",
            };
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Détection de Gemini CLI");
            return new AiProbe { Available = false, DisplayName = Name, Detail = "détection impossible" };
        }
    }

    public async Task<AiUsage?> ReadAsync(CancellationToken ct)
    {
        var now = _clock();

        // Task.Run : parcours de fichiers, à tenir hors du thread d'interface.
        var totals = await Task.Run(() =>
        {
            try
            {
                var since = UsageWindows.ScanStart(now);
                var entries = GeminiUsageReader.Read(_home, since);
                return entries.Count == 0 ? null : UsageAggregator.Aggregate(entries, now);
            }
            catch (Exception ex)
            {
                LogService.Warn(ex, "Lecture de la consommation Gemini CLI");
                return null;
            }
        }, ct).ConfigureAwait(false);

        if (totals is null) return null;

        return new AiUsage
        {
            ProviderId = Id,
            Name = Name,
            Glyph = "G",
            AccentColor = "#4285F4",
            Model = totals.Model,
            SessionTokens = totals.Session,
            DayTokens = totals.Day,
            MonthTokens = totals.Month,
            Requests = totals.Requests,
        };
    }
}
