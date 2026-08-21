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

    /// <summary>
    /// Intervalle minimal entre deux appels au quota.
    /// </summary>
    /// <remarks>
    /// Le bandeau se rafraîchit chaque minute, mais interroger le quota à cette cadence a valu des
    /// <c>HTTP 429</c> en usage réel — l'endpoint limite le débit, et les jauges disparaissaient à
    /// cause de notre propre insistance. Les fenêtres mesurées durent 5 h et 7 jours : cinq minutes
    /// de fraîcheur sont largement suffisantes.
    /// </remarks>
    private static readonly TimeSpan QuotaMinInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Âge au-delà duquel la dernière valeur connue n'est plus affichée.
    /// </summary>
    /// <remarks>
    /// Entre deux appels, les jauges gardent la valeur précédente : un pourcentage de quelques
    /// minutes vaut mieux qu'un vide, sur des fenêtres qui durent des heures. Passé ce délai en
    /// revanche, on préfère ne rien affirmer plutôt qu'afficher un chiffre périmé.
    /// </remarks>
    private static readonly TimeSpan QuotaMaxAge = TimeSpan.FromMinutes(15);

    private ClaudeLimitsClient.ClaudeLimits? _cachedLimits;
    private DateTime _cachedAt;
    private DateTime _lastAttempt;

    public string Id => "claude";
    public string Name => "Claude Code";

    /// <summary>
    /// Identité visuelle, déclarée une seule fois : la sonde et l'instantané la lisent ici.
    /// </summary>
    private const string PastilleGlyph = "✳";
    private const string PastilleAccent = "#D97757";

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
                return new AiProbe
                {
                    Available = false,
                    DisplayName = Name,
                    Glyph = PastilleGlyph,
                    AccentColor = PastilleAccent,
                    Detail = "non installé",
                };
            }

            var count = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).Take(1).Count();
            return new AiProbe
            {
                Available = true,
                DisplayName = Name,
                Glyph = PastilleGlyph,
                AccentColor = PastilleAccent,
                DataPath = root,
                Detail = count == 0 ? "installé, aucune donnée de session" : "",
            };
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Détection de Claude Code");
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

        // Task.Run : la lecture parcourt des centaines de fichiers (mesuré 2 s sur un profil réel).
        // Sans elle, tout ce travail s'exécute sur le thread d'interface avant le premier await,
        // et la fenêtre gèle à chaque affichage puis à chaque rafraîchissement.
        var totals = await Task.Run(() => ReadTotals(now), ct).ConfigureAwait(false);

        // Détecté mais inactif sur la période : un instantané à zéro plutôt que rien, pour que le
        // fournisseur garde son onglet. Absent du bandeau veut dire « pas installé ».
        if (totals is null && !Probe().Available) return null;
        totals ??= UsageAggregator.Empty;

        var limits = await ReadLimitsAsync(ct).ConfigureAwait(false);

        return new AiUsage
        {
            ProviderId = Id,
            Name = Name,
            Glyph = PastilleGlyph,
            AccentColor = PastilleAccent,
            UsageUrl = "https://claude.ai/new#settings/usage",
            Model = totals.Model,
            Cost = ClaudePricing.Format(totals.Cost),
            CostNote = "Équivalent API du mois en cours, estimé aux tarifs publics. "
                     + "Un abonnement Max ou Pro ne facture pas au jeton.",
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
        var now = _clock();

        // Trop tôt pour redemander : on rend la dernière valeur connue, ou rien si elle a vieilli.
        // C'est aussi ce qui évite de marteler l'endpoint après un 429.
        if (_lastAttempt != default && now - _lastAttempt < QuotaMinInterval) return FreshEnough(now);

        _lastAttempt = now;

        try
        {
            var path = ClaudeLimitsClient.CredentialsPath(_home);
            if (!File.Exists(path)) return FreshEnough(now);

            // Le jeton reste local à cette méthode : ni champ, ni journal, ni config.
            var token = ClaudeLimitsClient.ReadAccessToken(File.ReadAllText(path), DateTime.UtcNow);
            if (token is null) return FreshEnough(now);

            var (limits, failure) = await _limits.FetchAsync(token, ct).ConfigureAwait(false);
            if (limits is null)
            {
                NoteQuotaUnavailable(failure);
                // Un échec passager ne doit pas faire disparaître des jauges qui étaient justes il y
                // a une minute. Au-delà de QuotaMaxAge, en revanche, on ne prétend plus rien.
                return FreshEnough(now);
            }

            _cachedLimits = limits;
            _cachedAt = now;
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
            return FreshEnough(now);
        }
    }

    /// <summary>Dernière valeur connue, si elle n'a pas dépassé <see cref="QuotaMaxAge"/>.</summary>
    private ClaudeLimitsClient.ClaudeLimits? FreshEnough(DateTime now) =>
        _cachedLimits is not null && now - _cachedAt < QuotaMaxAge ? _cachedLimits : null;

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
