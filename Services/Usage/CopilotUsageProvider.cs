using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Consommation de GitHub Copilot CLI : jetons lus dans sa base SQLite locale.
/// </summary>
/// <remarks>
/// <b>Ni quota, ni coût.</b> Copilot facture des requêtes premium sur un abonnement, pas des jetons :
/// il n'y a pas de montant à calculer, et aucun pourcentage de limite dans la base. Les jauges
/// restent masquées et la colonne de coût affiche un tiret.
/// </remarks>
public sealed class CopilotUsageProvider : IUsageProvider
{
    private readonly string _home;
    private readonly Func<DateTime> _clock;

    public string Id => "copilot";
    public string Name => "Copilot CLI";

    /// <summary>
    /// Identité visuelle, déclarée une seule fois : la sonde et l'instantané la lisent ici.
    /// </summary>
    private const string PastilleGlyph = "⊕";
    private const string PastilleAccent = "#8957E5";

    public CopilotUsageProvider(string? home = null, Func<DateTime>? clock = null)
    {
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _clock = clock ?? (() => DateTime.Now);
    }

    public AiProbe Probe()
    {
        try
        {
            var path = CopilotUsageReader.DatabasePath(_home);
            if (!File.Exists(path))
            {
                // Le dossier peut exister sans la base : Copilot installé mais jamais utilisé.
                var installed = Directory.Exists(Path.GetDirectoryName(path)!);
                return new AiProbe
                {
                    Available = false,
                    DisplayName = Name,
                    Glyph = PastilleGlyph,
                    AccentColor = PastilleAccent,
                    Detail = installed ? Loc.T("Probe_NoSessionData") : Loc.T("Probe_NotInstalled"),
                };
            }

            return new AiProbe
            {
                Available = true,
                DisplayName = Name,
                Glyph = PastilleGlyph,
                AccentColor = PastilleAccent,
                DataPath = path,
            };
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Détection de Copilot CLI");
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

        var totals = await Task.Run(() =>
        {
            try
            {
                var entries = CopilotUsageReader.Read(_home, UsageWindows.ScanStart(now));
                return entries.Count == 0 ? null : UsageAggregator.Aggregate(entries, now);
            }
            catch (Exception ex)
            {
                LogService.Warn(ex, "Lecture de la consommation Copilot CLI");
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
