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

    /// <summary>
    /// Identité visuelle, déclarée une seule fois : la sonde et l'instantané la lisent ici.
    /// </summary>
    private const string PastilleGlyph = "G";
    private const string PastilleAccent = "#4285F4";

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
                return new AiProbe
                {
                    Available = false,
                    DisplayName = Name,
                    Glyph = PastilleGlyph,
                    AccentColor = PastilleAccent,
                    Detail = "non installé",
                };
            }

            var hasSession = Directory.EnumerateDirectories(root, "chats", SearchOption.AllDirectories)
                .SelectMany(d => Directory.EnumerateFiles(d, "session-*.*"))
                .Any();

            return new AiProbe
            {
                Available = true,
                DisplayName = Name,
                Glyph = PastilleGlyph,
                AccentColor = PastilleAccent,
                DataPath = root,
                Detail = hasSession ? "" : "installé, aucune donnée de session",
            };
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Détection de Gemini CLI");
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

        // Détecté mais inactif sur la période : un instantané à zéro plutôt que rien, pour que le
        // fournisseur garde son onglet. Absent du bandeau veut dire « pas installé ».
        if (totals is null && !Probe().Available) return null;
        totals ??= UsageAggregator.Empty;

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
