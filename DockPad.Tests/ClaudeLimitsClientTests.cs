using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class ClaudeLimitsClientTests
{
    // --- ReadAccessToken : pur, aucun accès disque

    private static string Credentials(string token, DateTime? expiresAt, bool wrapped = true)
    {
        var expires = expiresAt is null
            ? "null"
            : ((long)(expiresAt.Value.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var oauth = $$"""
        {"accessToken":"{{token}}","expiresAt":{{expires}},"subscriptionType":"max","rateLimitTier":"default_claude_max_20x"}
        """.Trim();
        return wrapped ? "{\"claudeAiOauth\":" + oauth + "}" : oauth;
    }

    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReadAccessToken_JetonValide_EstRetourne()
    {
        var json = Credentials("secret-abc", Now.AddHours(2));
        Assert.Equal("secret-abc", ClaudeLimitsClient.ReadAccessToken(json, Now));
    }

    [Fact]
    public void ReadAccessToken_JetonExpire_RetourneNull()
    {
        var json = Credentials("secret-abc", Now.AddHours(-1));
        Assert.Null(ClaudeLimitsClient.ReadAccessToken(json, Now));
    }

    [Fact]
    public void ReadAccessToken_JetonQuiExpireDansMoinsDUneMinute_RetourneNull()
    {
        // Marge d'une minute : un jeton qui expire pendant le vol ne sert à rien.
        var json = Credentials("secret-abc", Now.AddSeconds(30));
        Assert.Null(ClaudeLimitsClient.ReadAccessToken(json, Now));
    }

    [Fact]
    public void ReadAccessToken_ExpirationEnSecondes_EstAcceptee()
    {
        // Selon la version, expiresAt est en secondes ou en millisecondes depuis l'époque.
        var seconds = (long)(Now.AddHours(2) - DateTime.UnixEpoch).TotalSeconds;
        var json = "{\"claudeAiOauth\":{\"accessToken\":\"s\",\"expiresAt\":" + seconds + "}}";
        Assert.Equal("s", ClaudeLimitsClient.ReadAccessToken(json, Now));
    }

    [Fact]
    public void ReadAccessToken_SansExpiration_EstAcceptee()
    {
        var json = Credentials("secret-abc", null);
        Assert.Equal("secret-abc", ClaudeLimitsClient.ReadAccessToken(json, Now));
    }

    [Fact]
    public void ReadAccessToken_SansOAuthDeCompte_RetourneNull()
    {
        // Observé : le fichier ne contient que l'état OAuth de serveurs MCP.
        Assert.Null(ClaudeLimitsClient.ReadAccessToken("""{"mcpOAuth":{"x":1}}""", Now));
    }

    [Fact]
    public void ReadAccessToken_OAuthDeCompteExplicitementNull_RetourneNull()
    {
        // Piège : un JSON null se décode en élément présent. Tester la présence de la clé ferait
        // lire un état déconnecté comme « valeur disponible ».
        Assert.Null(ClaudeLimitsClient.ReadAccessToken("""{"claudeAiOauth":null}""", Now));
    }

    [Fact]
    public void ReadAccessToken_JetonVide_RetourneNull()
    {
        Assert.Null(ClaudeLimitsClient.ReadAccessToken("""{"claudeAiOauth":{"accessToken":""}}""", Now));
    }

    [Fact]
    public void ReadAccessToken_JsonInvalide_RetourneNull()
    {
        Assert.Null(ClaudeLimitsClient.ReadAccessToken("{pas du json", Now));
        Assert.Null(ClaudeLimitsClient.ReadAccessToken("", Now));
    }

    // --- ParseUsage : pur

    [Fact]
    public void ParseUsage_FormeHeritee_DonneLesDeuxFenetres()
    {
        var json = """
        {
          "five_hour": { "utilization": 62, "resets_at": "2026-08-20T14:00:00Z" },
          "seven_day": { "utilization": 44, "resets_at": "2026-08-24T00:00:00Z" }
        }
        """;

        var limits = ClaudeLimitsClient.ParseUsage(json);

        Assert.NotNull(limits);
        Assert.Equal(62, limits!.Session!.UsedPct);
        Assert.Equal(44, limits.Week!.UsedPct);
        Assert.Equal(new DateTime(2026, 8, 20, 14, 0, 0, DateTimeKind.Utc).ToLocalTime(),
                     limits.Session.ResetsAt);
    }

    [Fact]
    public void ParseUsage_FormeListe_DonneLesDeuxFenetres()
    {
        var json = """
        {
          "limits": [
            { "kind": "session",     "percent": 62, "resets_at": "2026-08-20T14:00:00Z" },
            { "kind": "weekly_all",  "percent": 44, "resets_at": "2026-08-24T00:00:00Z" },
            { "kind": "weekly_scoped", "percent": 10, "resets_at": "2026-08-24T00:00:00Z" }
          ]
        }
        """;

        var limits = ClaudeLimitsClient.ParseUsage(json);

        Assert.NotNull(limits);
        Assert.Equal(62, limits!.Session!.UsedPct);
        Assert.Equal(44, limits.Week!.UsedPct);
    }

    [Fact]
    public void ParseUsage_LesDeuxFormes_LHeriteeGagne()
    {
        var json = """
        {
          "five_hour": { "utilization": 62 },
          "limits": [ { "kind": "session", "percent": 99 } ]
        }
        """;

        Assert.Equal(62, ClaudeLimitsClient.ParseUsage(json)!.Session!.UsedPct);
    }

    [Fact]
    public void ParseUsage_PourcentageHorsBornes_EstRamene()
    {
        var json = """{ "five_hour": { "utilization": 143 }, "seven_day": { "utilization": -5 } }""";

        var limits = ClaudeLimitsClient.ParseUsage(json);

        Assert.Equal(100, limits!.Session!.UsedPct);
        Assert.Equal(0, limits.Week!.UsedPct);
    }

    [Fact]
    public void ParseUsage_PourcentageDecimal_EstArrondi()
    {
        var json = """{ "five_hour": { "utilization": 61.7 } }""";
        Assert.Equal(62, ClaudeLimitsClient.ParseUsage(json)!.Session!.UsedPct);
    }

    [Fact]
    public void ParseUsage_SansResetsAt_DonneUneFenetreSansDate()
    {
        var json = """{ "five_hour": { "utilization": 10 } }""";

        var session = ClaudeLimitsClient.ParseUsage(json)!.Session;

        Assert.NotNull(session);
        Assert.Null(session!.ResetsAt);
    }

    [Fact]
    public void ParseUsage_Plan_EstLuDansLesCredentials()
    {
        // Le plan ne vient pas de la réponse HTTP mais des credentials déjà lus.
        Assert.Equal("Max 20x", ClaudeLimitsClient.PlanLabel("max", "default_claude_max_20x"));
        Assert.Equal("Pro", ClaudeLimitsClient.PlanLabel("pro", "default_claude_pro"));
        Assert.Equal("", ClaudeLimitsClient.PlanLabel(null, null));
    }

    [Fact]
    public void ParseUsage_ReponseVideOuInvalide_RetourneNull()
    {
        Assert.Null(ClaudeLimitsClient.ParseUsage("{}"));
        Assert.Null(ClaudeLimitsClient.ParseUsage("pas du json"));
        Assert.Null(ClaudeLimitsClient.ParseUsage(""));
    }

    // --- FetchAsync : HttpClient injecté, jamais de réseau réel

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private const string UsageBody = """
    { "five_hour": { "utilization": 62, "resets_at": "2026-08-20T14:00:00Z" },
      "seven_day": { "utilization": 44, "resets_at": "2026-08-24T00:00:00Z" } }
    """;

    [Fact]
    public async Task FetchAsync_Succes_DonneLesFenetres()
    {
        var handler = new StubHandler(HttpStatusCode.OK, UsageBody);
        var client = new ClaudeLimitsClient(new HttpClient(handler));

        var (limits, failure) = await client.FetchAsync("jeton", CancellationToken.None);

        Assert.NotNull(limits);
        Assert.Equal(62, limits!.Session!.UsedPct);
        Assert.Equal("", failure);
    }

    [Fact]
    public async Task FetchAsync_PoseLesEnTetesAttendus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, UsageBody);
        var client = new ClaudeLimitsClient(new HttpClient(handler));

        await client.FetchAsync("jeton-xyz", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("jeton-xyz", request.Headers.Authorization.Parameter);
        Assert.Contains("oauth-2025-04-20", request.Headers.GetValues("anthropic-beta"));
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FetchAsync_ReponseEnEchec_NommeLeStatut(HttpStatusCode status)
    {
        // Le journal doit distinguer un 401 d'un 429 : c'est la première question qu'on se pose, et
        // un code de statut n'est pas une donnée sensible.
        var client = new ClaudeLimitsClient(new HttpClient(new StubHandler(status, "")));

        var (limits, failure) = await client.FetchAsync("jeton", CancellationToken.None);

        Assert.Null(limits);
        Assert.Contains(((int)status).ToString(), failure);
    }

    [Fact]
    public async Task FetchAsync_FormeInconnue_LeDitSansCiterLeCorps()
    {
        var client = new ClaudeLimitsClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, """{"autre_forme":1}""")));

        var (limits, failure) = await client.FetchAsync("jeton", CancellationToken.None);

        Assert.Null(limits);
        Assert.Contains("forme de réponse inconnue", failure);
        Assert.DoesNotContain("autre_forme", failure);   // jamais le corps dans le journal
    }

    [Fact]
    public async Task FetchAsync_JetonVide_NAppellePasLeReseau()
    {
        var handler = new StubHandler(HttpStatusCode.OK, UsageBody);
        var client = new ClaudeLimitsClient(new HttpClient(handler));

        var (limits, failure) = await client.FetchAsync("", CancellationToken.None);

        Assert.Null(limits);
        Assert.Equal("jeton absent", failure);
        Assert.Null(handler.LastRequest);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("réseau injoignable");
    }

    [Fact]
    public async Task FetchAsync_ReseauInjoignable_RetourneNullSansLever()
    {
        var client = new ClaudeLimitsClient(new HttpClient(new ThrowingHandler()));

        var (limits, failure) = await client.FetchAsync("jeton", CancellationToken.None);

        Assert.Null(limits);
        // Le type de l'exception, jamais son message : celui-ci pourrait embarquer l'URL.
        Assert.Equal(nameof(HttpRequestException), failure);
        Assert.DoesNotContain("injoignable", failure);
    }
}
