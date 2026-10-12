using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Quota d'Antigravity : deux réservoirs hebdomadaires, Gemini et modèles tiers, lus sur l'API
/// interne de Code Assist avec le jeton que le CLI <c>agy</c> range dans le Gestionnaire
/// d'identification Windows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le jeton est lu, jamais renouvelé.</b> Il ne vit qu'une heure ; le renouveler demanderait
/// l'identifiant client OAuth d'Antigravity, donc se faire passer pour son application. Quand il a
/// expiré, aucun appel n'a lieu et le dernier relevé reste affiché, daté. Ce n'est pas une
/// dégradation : le quota ne bouge que si l'on utilise Antigravity, et ce qui l'utilise renouvelle
/// aussi le jeton.
/// </para>
/// <para>
/// Aucune métrique de jetons : l'API n'en donne pas, et les bases locales du CLI n'ont pas été
/// examinées. Les colonnes affichent un tiret.
/// </para>
/// </remarks>
public sealed class AntigravityUsageProvider : IUsageProvider
{
    /// <summary>
    /// Intervalle minimal entre deux lectures : celui du quota Claude, pour la même raison — les
    /// fenêtres durent sept jours, et l'insistance vaut des 429.
    /// </summary>
    private static readonly TimeSpan QuotaMinInterval = TimeSpan.FromMinutes(5);

    private readonly Func<string?> _readCredential;
    private readonly string _home;
    private readonly string? _storePath;
    private readonly AntigravityQuotaClient _client;
    private readonly Func<DateTimeOffset> _clock;

    private readonly object _slotGate = new();

    private AntigravityReading? _lastReading;
    private bool _storeLoaded;
    private DateTimeOffset? _lastAttempt;

    /// <summary>Dernière cause d'échec, vide quand la lecture a réussi.</summary>
    private string _quotaFailure = "";

    /// <summary>Cause déjà journalisée : on ne journalise que les changements, comme pour Claude.</summary>
    private string _loggedFailure = "";

    public string Id => "antigravity";
    public string Name => "Antigravity";

    /// <summary>
    /// Identité visuelle, déclarée une seule fois : la sonde et l'instantané la lisent ici.
    /// </summary>
    private const string PastilleGlyph = "A";
    private const string PastilleAccent = ProviderLogos.AntigravityAccent;

    public AntigravityUsageProvider(Func<string?>? readCredential = null, string? home = null, string? storePath = null,
                                    HttpClient? http = null, Func<DateTimeOffset>? clock = null)
    {
        _readCredential = readCredential ?? (() => WindowsCredential.ReadGeneric(AntigravityQuota.CredentialTarget));
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Résolu à l'usage et non ici : le registre est construit tôt, et un outil de capture doit
        // pouvoir poser DOCKPAD_PROFILE_DIR avant le premier accès au profil.
        _storePath = storePath;
        _client = new AntigravityQuotaClient(http);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    private string CliFolder => Path.Combine(_home, ".gemini", "antigravity-cli");

    private string StorePath => _storePath ?? AppPaths.File(AntigravityQuotaStore.FileName);

    public AiProbe Probe()
    {
        try
        {
            var hasCredential = _readCredential() != null;
            var hasCliFolder = Directory.Exists(CliFolder);

            return new AiProbe
            {
                Available = hasCredential || hasCliFolder,
                DisplayName = Name,
                Glyph = PastilleGlyph,
                AccentColor = PastilleAccent,
                DataPath = hasCliFolder ? CliFolder : "",
                Detail = hasCredential || hasCliFolder ? "" : Loc.T("Probe_NotInstalled"),
            };
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Détection d'Antigravity");
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
        if (!Probe().Available) return null;

        var reading = await ReadQuotaAsync(ct).ConfigureAwait(false);
        var (gemini, thirdParty) = AntigravityQuota.Windows(reading, _clock());
        var hasQuota = gemini != null || thirdParty != null;

        return new AiUsage
        {
            ProviderId = Id,
            Name = Name,
            Glyph = PastilleGlyph,
            AccentColor = PastilleAccent,
            Session = gemini,
            Week = thirdParty,
            QuotaNotice = hasQuota ? "" : Loc.T("Usage_Antigravity_QuotaNotice"),
            QuotaNoticeNote = hasQuota ? "" : Loc.F("Usage_Antigravity_QuotaNoticeNote", CauseOfMissingQuota()),
        };
    }

    /// <summary>
    /// Le relevé le plus récent : celui de l'API si une lecture est due et possible, sinon le
    /// dernier connu — en mémoire, ou à défaut sur le disque.
    /// </summary>
    private async Task<AntigravityReading?> ReadQuotaAsync(CancellationToken ct)
    {
        var now = _clock();
        DateTimeOffset? previousAttempt;

        // Le bandeau et dockpad_usage_get partagent cette instance et peuvent lire en même temps :
        // sans verrou, deux lectures franchiraient la garde avant que l'une ait réservé le créneau.
        lock (_slotGate)
        {
            LoadStoredReadingOnce();
            if (_lastAttempt != null && now - _lastAttempt.Value < QuotaMinInterval) return _lastReading;

            previousAttempt = _lastAttempt;
            _lastAttempt = now;
        }

        try
        {
            // Le jeton reste local à cette méthode : ni champ, ni journal, ni fichier.
            var token = AntigravityQuota.ReadAccessToken(_readCredential(), now);
            if (token == null)
            {
                NoteQuotaUnavailable("access token missing or expired");
                return _lastReading;
            }

            var (reading, failure) = await _client.FetchAsync(token, now, ct).ConfigureAwait(false);
            if (reading == null)
            {
                NoteQuotaUnavailable(failure);
                return _lastReading;
            }

            NoteQuotaRestored();
            _lastReading = reading;
            SaveReading(reading);
            return reading;
        }
        catch (OperationCanceledException)
        {
            // Le créneau est rendu : une annulation n'a rien tenté. Seulement s'il est encore le
            // nôtre — une lecture plus récente l'a peut-être réservé entre-temps.
            lock (_slotGate)
            {
                if (_lastAttempt == now) _lastAttempt = previousAttempt;
            }
            throw;
        }
        catch (Exception ex)
        {
            // Le type, jamais le message : il pourrait citer le contenu de l'identifiant.
            NoteQuotaUnavailable(ex.GetType().Name);
            return _lastReading;
        }
    }

    private void LoadStoredReadingOnce()
    {
        if (_storeLoaded) return;
        _storeLoaded = true;
        _lastReading = AntigravityQuotaStore.Load(StorePath);
    }

    private void SaveReading(AntigravityReading reading)
    {
        try
        {
            AntigravityQuotaStore.Save(StorePath, reading);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Le relevé reste en mémoire : seul le redémarrage suivant le perdra.
            LogService.Warn(ex, "Enregistrement du dernier relevé du quota Antigravity");
        }
    }

    /// <summary>
    /// Pourquoi aucune jauge n'est affichée : la dernière cause d'échec, ou, faute d'échec, un relevé
    /// dont les fenêtres sont toutes passées.
    /// </summary>
    private string CauseOfMissingQuota() =>
        _quotaFailure.Length > 0 ? _quotaFailure : "quota windows of the last reading have expired";

    private void NoteQuotaUnavailable(string cause)
    {
        _quotaFailure = cause;
        if (_loggedFailure == cause) return;
        _loggedFailure = cause;
        LogService.Info($"Quota Antigravity non relu ({cause}) — dernier relevé conservé");
    }

    private void NoteQuotaRestored()
    {
        _quotaFailure = "";
        if (_loggedFailure.Length == 0) return;
        _loggedFailure = "";
        LogService.Info("Quota Antigravity de nouveau lisible");
    }
}
