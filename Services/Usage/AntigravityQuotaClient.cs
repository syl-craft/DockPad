using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Services.Usage;

/// <summary>
/// Lit le quota Antigravity sur l'API interne de Code Assist : <c>loadCodeAssist</c> pour le
/// projet, puis <c>fetchAvailableModels</c> pour les fractions restantes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ce chemin n'est jamais critique.</b> L'API n'est pas documentée et cassera un jour : tout
/// échec rend <c>null</c> et une raison courte, jamais une exception.
/// </para>
/// <para>
/// <b>Traitement du secret.</b> Le jeton est reçu en paramètre, n'est ni gardé ni journalisé, et
/// ne part que vers <c>cloudcode-pa.googleapis.com</c>.
/// </para>
/// </remarks>
public sealed class AntigravityQuotaClient
{
    private const string BaseUrl = "https://cloudcode-pa.googleapis.com/v1internal:";

    private readonly HttpClient _http;

    public AntigravityQuotaClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// L'en-tête <c>User-Agent</c> envoyé : DockPad sous son nom, avec le mot « antigravity ».
    /// </summary>
    /// <remarks>
    /// <b>Sans ce mot, l'API ne répond pas</b> : <c>loadCodeAssist</c> rend 200 sans projet et
    /// <c>fetchAvailableModels</c> rend 403 — vérifié sur le compte réel. Le serveur l'accepte
    /// n'importe où dans la chaîne, donc DockPad n'a pas à se faire passer pour le client
    /// d'Antigravity : il s'annonce, et dit pour qui il lit. Si Google resserre ce filtre, la
    /// lecture échoue proprement et le dernier relevé reste affiché, daté.
    /// </remarks>
    public static string UserAgent(string versionText)
    {
        var version = versionText.TrimStart('v');
        return version.Length > 0
            ? $"DockPad/{version} (antigravity quota reader)"
            : "DockPad (antigravity quota reader)";
    }

    /// <returns>
    /// Le relevé, et une raison d'échec quand il vaut <c>null</c>. La raison est destinée au journal
    /// et à l'infobulle : un code de statut, un type d'exception — jamais le jeton ni le corps.
    /// </returns>
    public async Task<(AntigravityReading? Reading, string Failure)> FetchAsync(string accessToken, DateTimeOffset now,
                                                                                CancellationToken ct)
    {
        try
        {
            var (assist, assistFailure) = await PostAsync("loadCodeAssist", "{}", accessToken, ct).ConfigureAwait(false);
            if (assist == null) return (null, assistFailure);

            var project = AntigravityQuota.ReadProject(assist);
            // Diagnostics, donc en anglais et non traduits, comme ceux du quota Claude.
            if (project == null) return (null, "no project in loadCodeAssist");

            var body = JsonSerializer.Serialize(new { project });
            var (models, modelsFailure) = await PostAsync("fetchAvailableModels", body, accessToken, ct).ConfigureAwait(false);
            if (models == null) return (null, modelsFailure);

            var reading = AntigravityQuota.ParseModels(models, now);
            return reading == null
                ? (null, $"unknown response shape ({models.Length} bytes)")
                : (reading, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Le type de l'exception, jamais son message : celui-ci pourrait embarquer l'URL.
            return (null, ex.GetType().Name);
        }
    }

    private async Task<(string? Body, string Failure)> PostAsync(string method, string json, string accessToken,
                                                                 CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + method);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent(AppInfo.VersionText));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return (null, $"{method}: HTTP {(int)response.StatusCode} {response.StatusCode}");

        return (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), "");
    }
}
