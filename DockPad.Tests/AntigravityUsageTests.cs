using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services.Usage;

using DockPad.Services.Localization;

namespace DockPad.Tests;

public class AntigravityQuotaTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 15, 0, 0, TimeSpan.Zero);

    private static string Credential(string token, DateTimeOffset expiry) => JsonSerializer.Serialize(new
    {
        token = new { access_token = token, token_type = "Bearer", refresh_token = "r", expiry = expiry.ToString("o") },
        auth_method = "consumer",
    });

    /// <summary>Réponse réduite de fetchAvailableModels, à la forme relevée sur le poste.</summary>
    public static string Models(double gemini, double thirdParty, DateTimeOffset reset) => $$"""
        {
          "models": {
            "gemini-3.1-pro-high": { "modelProvider": "MODEL_PROVIDER_GOOGLE",
              "quotaInfo": { "remainingFraction": {{gemini.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "resetTime": "{{reset.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}" } },
            "gemini-3-flash": { "modelProvider": "MODEL_PROVIDER_GOOGLE",
              "quotaInfo": { "remainingFraction": 0.99, "resetTime": "{{reset.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}" } },
            "claude-sonnet-4-6": { "modelProvider": "MODEL_PROVIDER_ANTHROPIC",
              "quotaInfo": { "remainingFraction": {{thirdParty.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "resetTime": "{{reset.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}" } },
            "gpt-oss-120b-medium": { "modelProvider": "MODEL_PROVIDER_OPENAI",
              "quotaInfo": { "remainingFraction": 1, "resetTime": "{{reset.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}" } },
            "tab_flash_lite_preview": { "modelProvider": "MODEL_PROVIDER_GOOGLE",
              "quotaInfo": { "remainingFraction": 0.01 } }
          },
          "defaultAgentModelId": "gemini-3-flash"
        }
        """;

    [Fact]
    public void ReadAccessToken_JetonValide_Rendu()
    {
        Assert.Equal("abc", AntigravityQuota.ReadAccessToken(Credential("abc", Now.AddMinutes(30)), Now));
    }

    [Fact]
    public void ReadAccessToken_ExpireOuSurLePointDeLEtre_Null()
    {
        Assert.Null(AntigravityQuota.ReadAccessToken(Credential("abc", Now.AddMinutes(-1)), Now));
        // Un jeton qui expire pendant le vol ne sert à rien : même marge que pour Claude.
        Assert.Null(AntigravityQuota.ReadAccessToken(Credential("abc", Now.AddSeconds(30)), Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas du json")]
    [InlineData("""{"token":null}""")]
    [InlineData("""{"token":{"access_token":""}}""")]
    // Sans date lisible, rien ne prouve que le jeton est encore valide : on ne l'envoie pas.
    [InlineData("""{"token":{"access_token":"abc"}}""")]
    [InlineData("""{"token":{"access_token":"abc","expiry":"demain"}}""")]
    public void ReadAccessToken_IdentifiantInexploitable_Null(string? credential)
    {
        Assert.Null(AntigravityQuota.ReadAccessToken(credential, Now));
    }

    [Theory]
    [InlineData("""{"cloudaicompanionProject":"proj-1"}""", "proj-1")]
    [InlineData("""{"cloudaicompanionProject":{"id":"proj-2"}}""", "proj-2")]
    [InlineData("""{"currentTier":{}}""", null)]
    [InlineData("pas du json", null)]
    public void ReadProject_ChaineObjetOuAbsent(string json, string? expected)
    {
        Assert.Equal(expected, AntigravityQuota.ReadProject(json));
    }

    [Fact]
    public void ParseModels_DeuxReservoirs_LePlusBasRestantDuGroupe()
    {
        var reset = Now.AddDays(7);

        var reading = AntigravityQuota.ParseModels(Models(gemini: 0.62, thirdParty: 0.25, reset), Now);

        Assert.NotNull(reading);
        Assert.Equal(38, reading?.Gemini?.UsedPct);
        Assert.Equal(reset, reading?.Gemini?.ResetsAt);
        Assert.Equal(75, reading?.ThirdParty?.UsedPct);
        Assert.Equal(Now, reading?.ObservedAt);
    }

    [Fact]
    public void ParseModels_ModeleSansRemiseAZero_Ignore()
    {
        // tab_flash_lite_preview a 1 % restant mais aucun resetTime : ce n'est pas un quota de
        // l'utilisateur, il ne doit pas faire passer la jauge Gemini à 99 %.
        var reading = AntigravityQuota.ParseModels(Models(gemini: 0.9, thirdParty: 1, Now.AddDays(7)), Now);

        Assert.Equal(10, reading?.Gemini?.UsedPct);
    }

    [Fact]
    public void ParseModels_FractionAbsente_ReservoirEpuise()
    {
        // Proto3 n'écrit pas un champ à zéro : un quota épuisé arrive sans remainingFraction.
        const string json = """
            { "models": { "gemini-3-flash": { "modelProvider": "MODEL_PROVIDER_GOOGLE",
                "quotaInfo": { "resetTime": "2026-10-17T15:03:17Z" } } } }
            """;

        var reading = AntigravityQuota.ParseModels(json, Now);

        Assert.Equal(100, reading?.Gemini?.UsedPct);
        Assert.Null(reading?.ThirdParty);
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("""{"error":{"code":403}}""")]
    [InlineData("""{"models":{"tab_flash_lite_preview":{"quotaInfo":{"remainingFraction":1}}}}""")]
    public void ParseModels_RienDExploitable_Null(string json)
    {
        Assert.Null(AntigravityQuota.ParseModels(json, Now));
    }

    [Fact]
    public void Windows_ReleveFrais_NonDateEtLibelles()
    {
        var reading = new AntigravityReading(Now, new AntigravityPool(38, Now.AddDays(7)), new AntigravityPool(75, Now.AddDays(7)));

        var (gemini, thirdParty) = AntigravityQuota.Windows(reading, Now.AddMinutes(10));

        Assert.Equal(38, gemini?.UsedPct);
        Assert.Null(gemini?.ObservedAt);
        Assert.Equal("Gemini", gemini?.Label);
        Assert.Equal(75, thirdParty?.UsedPct);
        Assert.Equal("Claude · GPT", thirdParty?.Label);
        Assert.Equal(Now.AddDays(7).LocalDateTime, gemini?.ResetsAt);
    }

    [Fact]
    public void Windows_ReleveAncien_DateAvecSonHeure()
    {
        var reading = new AntigravityReading(Now, new AntigravityPool(38, Now.AddDays(7)), null);

        var (gemini, thirdParty) = AntigravityQuota.Windows(reading, Now.AddHours(3));

        Assert.Equal(Now.LocalDateTime, gemini?.ObservedAt);
        Assert.Null(thirdParty);
    }

    [Fact]
    public void Windows_RemiseAZeroPassee_JaugeMasquee()
    {
        // Le relevé décrivait une fenêtre qui n'existe plus : la montrer affirmerait une
        // consommation déjà effacée.
        var reading = new AntigravityReading(Now, new AntigravityPool(38, Now.AddDays(1)), new AntigravityPool(10, Now.AddDays(3)));

        var (gemini, thirdParty) = AntigravityQuota.Windows(reading, Now.AddDays(2));

        Assert.Null(gemini);
        Assert.NotNull(thirdParty);
    }

    [Theory]
    [InlineData("v1.28.0", "DockPad/1.28.0 (antigravity quota reader)")]
    [InlineData("", "DockPad (antigravity quota reader)")]
    public void UserAgent_NommeDockPadEtSatisfaitLeFiltre(string versionText, string expected)
    {
        Assert.Equal(expected, AntigravityQuotaClient.UserAgent(versionText));
    }

    [Fact]
    public void Windows_SansReleve_RienAAfficher()
    {
        Assert.Equal((null, null), AntigravityQuota.Windows(null, Now));
    }
}

public class AntigravityQuotaStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"agystore_{Guid.NewGuid():N}");
    private string StorePath => Path.Combine(_dir, "antigravity-quota.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveLoad_AllerRetour()
    {
        var at = new DateTimeOffset(2026, 10, 10, 17, 0, 0, TimeSpan.FromHours(2));
        var reading = new AntigravityReading(at, new AntigravityPool(38, at.AddDays(7)), null);

        AntigravityQuotaStore.Save(StorePath, reading);

        Assert.Equal(reading, AntigravityQuotaStore.Load(StorePath));
    }

    [Fact]
    public void Load_FichierAbsentOuAbime_Null()
    {
        Assert.Null(AntigravityQuotaStore.Load(StorePath));

        Directory.CreateDirectory(_dir);
        File.WriteAllText(StorePath, "{ pas du json");
        Assert.Null(AntigravityQuotaStore.Load(StorePath));
    }
}

public class AntigravityUsageProviderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"agyprov_{Guid.NewGuid():N}");
    private string StorePath => Path.Combine(_home, "profile", "antigravity-quota.json");

    private DateTimeOffset _now = new(2026, 10, 10, 15, 0, 0, TimeSpan.Zero);

    public AntigravityUsageProviderTests()
    {
        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("fr"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private const string Token = "jeton-secret-de-test";

    private string ValidCredential() => JsonSerializer.Serialize(new
    {
        token = new { access_token = Token, expiry = _now.AddMinutes(50).ToString("o") },
        auth_method = "consumer",
    });

    private string ExpiredCredential() => JsonSerializer.Serialize(new
    {
        token = new { access_token = Token, expiry = _now.AddMinutes(-5).ToString("o") },
    });

    /// <summary>
    /// Répond comme les deux endpoints, filtre compris : sans « antigravity » dans le User-Agent,
    /// le vrai serveur rend un <c>loadCodeAssist</c> sans projet et refuse les modèles (403).
    /// </summary>
    private sealed class FakeCloudCode(string modelsJson, HttpStatusCode modelsStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls;
        public readonly List<string> Hosts = [];
        public readonly List<string?> Authorizations = [];
        public readonly List<string> UserAgents = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Hosts.Add(request.RequestUri?.Host ?? "");
            Authorizations.Add(request.Headers.Authorization?.ToString());
            var userAgent = request.Headers.UserAgent.ToString();
            UserAgents.Add(userAgent);
            var recognized = userAgent.Contains("antigravity", StringComparison.OrdinalIgnoreCase);

            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith(":loadCodeAssist"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(recognized
                        ? """{"cloudaicompanionProject":"proj-1","currentTier":{"id":"free-tier"}}"""
                        : """{"currentTier":{"id":"free-tier"}}"""),
                });

            if (recognized == false) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            return Task.FromResult(new HttpResponseMessage(modelsStatus) { Content = new StringContent(modelsJson) });
        }
    }

    private sealed class ThrowIfCalled : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("aucun appel réseau attendu");
    }

    private AntigravityUsageProvider Provider(string? credential, HttpMessageHandler handler, bool withCliFolder = true)
    {
        if (withCliFolder) Directory.CreateDirectory(Path.Combine(_home, ".gemini", "antigravity-cli"));
        return new AntigravityUsageProvider(
            readCredential: () => credential,
            home: _home,
            storePath: StorePath,
            http: new HttpClient(handler),
            clock: () => _now);
    }

    private string ModelsNow(double gemini = 0.62, double thirdParty = 0.25) =>
        AntigravityQuotaTests.Models(gemini, thirdParty, _now.AddDays(7));

    [Fact]
    public void Probe_NiIdentifiantNiDossier_NonDisponible()
    {
        var probe = Provider(null, new ThrowIfCalled(), withCliFolder: false).Probe();

        Assert.False(probe.Available);
        Assert.Equal("A", probe.Glyph);
    }

    [Fact]
    public void Probe_IdentifiantSeul_Disponible()
    {
        Assert.True(Provider(ValidCredential(), new ThrowIfCalled(), withCliFolder: false).Probe().Available);
    }

    [Fact]
    public void Probe_DossierSeul_Disponible()
    {
        Assert.True(Provider(null, new ThrowIfCalled()).Probe().Available);
    }

    [Fact]
    public async Task ReadAsync_NonInstalle_Null()
    {
        Assert.Null(await Provider(null, new ThrowIfCalled(), withCliFolder: false).ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_JetonValide_DeuxJaugesEtReleveEnregistre()
    {
        var cloud = new FakeCloudCode(ModelsNow());

        var usage = await Provider(ValidCredential(), cloud).ReadAsync(CancellationToken.None);

        Assert.Equal("antigravity", usage?.ProviderId);
        Assert.Equal(38, usage?.Session?.UsedPct);
        Assert.Equal("Gemini", usage?.Session?.Label);
        Assert.Null(usage?.Session?.ObservedAt);
        Assert.Equal(75, usage?.Week?.UsedPct);
        Assert.Equal("Claude · GPT", usage?.Week?.Label);
        Assert.Equal("", usage?.QuotaNotice);
        Assert.Equal(2, cloud.Calls);
        Assert.True(File.Exists(StorePath));
    }

    [Fact]
    public async Task ReadAsync_LeJetonNePartQueVersCloudCodeEtNEstJamaisEcrit()
    {
        var cloud = new FakeCloudCode(ModelsNow());

        await Provider(ValidCredential(), cloud).ReadAsync(CancellationToken.None);

        Assert.All(cloud.Hosts, host => Assert.Equal("cloudcode-pa.googleapis.com", host));
        Assert.All(cloud.Authorizations, auth => Assert.Equal($"Bearer {Token}", auth));
        Assert.DoesNotContain(Token, File.ReadAllText(StorePath));
    }

    [Fact]
    public async Task ReadAsync_DockPadSAnnonceSousSonNom()
    {
        // Le serveur exige « antigravity » dans le User-Agent ; DockPad le satisfait sans se faire
        // passer pour le client d'Antigravity : son propre nom vient en tête.
        var cloud = new FakeCloudCode(ModelsNow());

        await Provider(ValidCredential(), cloud).ReadAsync(CancellationToken.None);

        Assert.Equal(2, cloud.UserAgents.Count);
        Assert.All(cloud.UserAgents, userAgent => Assert.StartsWith("DockPad/", userAgent));
        Assert.All(cloud.UserAgents, userAgent => Assert.Contains("antigravity", userAgent));
    }

    [Fact]
    public async Task ReadAsync_JetonExpire_AucunAppelEtDernierReleveDate()
    {
        var observed = _now.AddHours(-3);
        AntigravityQuotaStore.Save(StorePath,
            new AntigravityReading(observed, new AntigravityPool(20, _now.AddDays(4)), null));

        var cloud = new FakeCloudCode(ModelsNow());

        var usage = await Provider(ExpiredCredential(), cloud).ReadAsync(CancellationToken.None);

        // Compté et non levé : le client absorbe les exceptions, un appel interdit passerait inaperçu.
        Assert.Equal(0, cloud.Calls);
        Assert.Equal(20, usage?.Session?.UsedPct);
        Assert.Equal(observed.LocalDateTime, usage?.Session?.ObservedAt);
        Assert.Null(usage?.Week);
        Assert.Equal("", usage?.QuotaNotice);
    }

    [Fact]
    public async Task ReadAsync_JetonExpireSansReleve_Notice()
    {
        var cloud = new FakeCloudCode(ModelsNow());

        var usage = await Provider(ExpiredCredential(), cloud).ReadAsync(CancellationToken.None);

        Assert.Equal(0, cloud.Calls);
        Assert.Null(usage?.Session);
        Assert.Null(usage?.Week);
        Assert.Equal("Quota Antigravity indisponible", usage?.QuotaNotice);
        Assert.Contains("access token missing or expired", usage?.QuotaNoticeNote);
    }

    [Fact]
    public async Task ReadAsync_AuPlusUnAppelParIntervalle()
    {
        var cloud = new FakeCloudCode(ModelsNow());
        var provider = Provider(ValidCredential(), cloud);

        await provider.ReadAsync(CancellationToken.None);
        _now = _now.AddMinutes(2);
        var cached = await provider.ReadAsync(CancellationToken.None);
        _now = _now.AddMinutes(4);
        await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(38, cached?.Session?.UsedPct);
        Assert.Equal(4, cloud.Calls);
    }

    [Fact]
    public async Task ReadAsync_Refuse_PasDExceptionEtDernierReleveConserve()
    {
        var observed = _now.AddHours(-1);
        AntigravityQuotaStore.Save(StorePath,
            new AntigravityReading(observed, new AntigravityPool(20, _now.AddDays(4)), null));

        var usage = await Provider(ValidCredential(), new FakeCloudCode("""{"error":{}}""", HttpStatusCode.Forbidden))
            .ReadAsync(CancellationToken.None);

        Assert.Equal(20, usage?.Session?.UsedPct);
        Assert.Equal(observed.LocalDateTime, usage?.Session?.ObservedAt);
    }

    [Fact]
    public async Task ReadAsync_RefuseSansReleve_NoticeAvecLeCode()
    {
        var usage = await Provider(ValidCredential(), new FakeCloudCode("{}", HttpStatusCode.Forbidden))
            .ReadAsync(CancellationToken.None);

        Assert.Equal("Quota Antigravity indisponible", usage?.QuotaNotice);
        Assert.Contains("HTTP 403", usage?.QuotaNoticeNote);
    }

    [Fact]
    public async Task ReadAsync_FormeInconnue_NoticeEtRienDEcrit()
    {
        var usage = await Provider(ValidCredential(), new FakeCloudCode("""{"models":{}}"""))
            .ReadAsync(CancellationToken.None);

        Assert.Contains("unknown response shape", usage?.QuotaNoticeNote);
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void Registre_ContientAntigravity()
    {
        Assert.Contains("antigravity", UsageProviderRegistry.All.Select(p => p.Id));
    }
}
