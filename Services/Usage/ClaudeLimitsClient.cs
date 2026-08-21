using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.Usage;

/// <summary>
/// Lit les pourcentages de quota officiels de Claude : fenêtre de session (5 h) et fenêtre
/// hebdomadaire, avec leurs heures de remise à zéro.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ce chemin n'est jamais critique.</b> L'endpoint <c>oauth/usage</c> n'est pas documenté et
/// cassera un jour : tout échec — jeton absent, expiré, 401, réseau coupé, forme de réponse
/// inconnue — se traduit par <c>null</c>, donc par des jauges masquées, jamais par une erreur
/// visible ni par une perte des métriques de jetons.
/// </para>
/// <para>
/// <b>Traitement du secret.</b> Le jeton d'accès est lu depuis
/// <c>%USERPROFILE%\.claude\.credentials.json</c>, gardé en mémoire jusqu'à une minute avant son
/// expiration, et n'est <b>jamais</b> journalisé, ni recopié dans une config, ni envoyé ailleurs
/// que sur <c>api.anthropic.com</c>. Windows n'a pas de keychain : ce fichier est la seule source.
/// </para>
/// </remarks>
public sealed class ClaudeLimitsClient
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";

    /// <summary>Marge avant expiration : un jeton qui expire pendant le vol ne sert à rien.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;

    /// <summary>Les deux fenêtres de quota lues sur l'endpoint.</summary>
    public sealed record ClaudeLimits(UsageWindow? Session, UsageWindow? Week);

    public ClaudeLimitsClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>Chemin du fichier de credentials pour un dossier de départ donné.</summary>
    public static string CredentialsPath(string home) =>
        Path.Combine(home, ".claude", ".credentials.json");

    /// <summary>
    /// Jeton d'accès contenu dans le JSON de credentials, ou <c>null</c> s'il est absent, vide,
    /// expiré, ou si le fichier ne porte pas d'OAuth de compte.
    /// </summary>
    public static string? ReadAccessToken(string credentialsJson, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(credentialsJson);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return null;
            // Un JSON `null` explicite se décode en élément présent : tester la seule présence de la
            // clé ferait lire un état déconnecté comme « valeur disponible ».
            if (oauth.ValueKind != JsonValueKind.Object) return null;

            if (!oauth.TryGetProperty("accessToken", out var tokenElement)) return null;
            var token = tokenElement.GetString();
            if (string.IsNullOrEmpty(token)) return null;

            if (ExpiresAt(oauth) is { } expiresAt && expiresAt <= now.ToUniversalTime() + ExpiryMargin)
                return null;

            return token;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Analyse la réponse de <c>oauth/usage</c>. Deux formes coexistent : les champs hérités
    /// <c>five_hour</c>/<c>seven_day</c>, et la liste généralisée <c>limits[]</c>. Les champs
    /// hérités priment quand les deux sont présents — c'est la forme dont la sémantique est sûre.
    /// </summary>
    public static ClaudeLimits? ParseUsage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var session = Window(root, "five_hour", "utilization");
            var week = Window(root, "seven_day", "utilization");

            // La liste est consultée dès qu'une des deux fenêtres manque, et non seulement quand les
            // deux manquent : une réponse mixte — `five_hour` plus `limits[weekly_all]` — perdait
            // sinon la jauge hebdomadaire alors que la donnée était dans la charge utile. La
            // priorité des champs hérités reste assurée par les `??=` ci-dessous.
            if ((session is null || week is null) && root.TryGetProperty("limits", out var limits)
                && limits.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in limits.EnumerateArray())
                {
                    var kind = entry.TryGetProperty("kind", out var k) ? k.GetString() : null;
                    var window = WindowFrom(entry, "percent");
                    if (window is null) continue;
                    if (kind == "session") session ??= window;
                    else if (kind == "weekly_all") week ??= window;
                }
            }

            return session is null && week is null ? null : new ClaudeLimits(session, week);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Interroge l'endpoint de quota. Ne lève jamais : l'appelant masque les jauges et garde les
    /// métriques de jetons.
    /// </summary>
    /// <returns>
    /// Le quota, et une raison d'échec quand il vaut <c>null</c>. La raison est destinée au journal
    /// et ne contient que des faits non sensibles — un code de statut, un type d'exception : jamais
    /// le jeton, jamais le corps de la réponse.
    /// </returns>
    public async Task<(ClaudeLimits? Limits, string Failure)> FetchAsync(string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken)) return (null, "jeton absent");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("anthropic-beta", BetaHeader);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"HTTP {(int)response.StatusCode} {response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var limits = ParseUsage(body);
            return limits is null
                ? (null, $"forme de réponse inconnue ({body.Length} octets)")
                : (limits, "");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Le type de l'exception, jamais son message : celui-ci pourrait embarquer l'URL ou des
            // en-têtes.
            return (null, ex.GetType().Name);
        }
    }

    private static DateTime? ExpiresAt(JsonElement oauth)
    {
        if (!oauth.TryGetProperty("expiresAt", out var raw)) return null;

        double value;
        switch (raw.ValueKind)
        {
            case JsonValueKind.Number when raw.TryGetDouble(out var n): value = n; break;
            case JsonValueKind.String when double.TryParse(raw.GetString(), NumberStyles.Any,
                                                           CultureInfo.InvariantCulture, out var s): value = s; break;
            default: return null;
        }
        if (value <= 0) return null;

        // Selon la version, l'horodatage est en secondes ou en millisecondes depuis l'époque.
        var seconds = value > 10_000_000_000 ? value / 1000 : value;
        return DateTime.UnixEpoch.AddSeconds(seconds);
    }

    private static UsageWindow? Window(JsonElement root, string property, string percentName) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Object
            ? WindowFrom(element, percentName)
            : null;

    private static UsageWindow? WindowFrom(JsonElement element, string percentName)
    {
        if (!element.TryGetProperty(percentName, out var percent)) return null;
        if (percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var value)) return null;

        // La valeur est un pourcentage, pas une fraction. Pas d'heuristique « si <= 1 alors
        // fraction » : une utilisation réelle de 1 % deviendrait 100 %, donc une alerte rouge fausse.
        var used = (int)Math.Clamp(Math.Round(value), 0, 100);

        DateTime? resetsAt = null;
        if (element.TryGetProperty("resets_at", out var reset) && reset.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            resetsAt = parsed.LocalDateTime;
        }

        return new UsageWindow { UsedPct = used, ResetsAt = resetsAt };
    }


}
