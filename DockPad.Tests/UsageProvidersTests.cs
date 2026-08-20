using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class DemoUsageProviderTests
{
    private static DemoUsageProvider Provider(Func<DateTime>? clock = null) =>
        new("demo", "Démo", "D", "#7C3AED",
            new DemoUsageProvider.DemoValues("claude-sonnet-5", 12_400, 86_000, 1_200_000, 47, "$4",
                                             62, TimeSpan.FromHours(2), 44, TimeSpan.FromDays(4)),
            clock);

    [Fact]
    public void Probe_EstDisponibleDemoEtMasqueParDefaut()
    {
        var probe = Provider().Probe();

        Assert.True(probe.Available);
        Assert.True(probe.IsDemo);
        Assert.True(probe.HiddenByDefault);
    }

    [Fact]
    public async Task ReadAsync_DeuxAppels_DonnentLesMemesValeurs()
    {
        // Une capture de documentation doit être reproductible : aucune valeur aléatoire.
        var provider = Provider();

        var a = await provider.ReadAsync(CancellationToken.None);
        var b = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(a!.DayTokens, b!.DayTokens);
        Assert.Equal(a.Session!.UsedPct, b.Session!.UsedPct);
        Assert.Equal(a.Cost, b.Cost);
    }

    [Fact]
    public async Task ReadAsync_ResetsToujoursDansLeFutur()
    {
        // Les décalages sont relatifs à l'horloge : une date absolue serait périmée dès le lendemain.
        var now = new DateTime(2030, 1, 1, 8, 0, 0);
        var usage = await Provider(() => now).ReadAsync(CancellationToken.None);

        Assert.True(usage!.Session!.ResetsAt > now);
        Assert.True(usage.Week!.ResetsAt > now);
    }

    [Fact]
    public async Task ReadAsync_PourcentagesDansLesBornes()
    {
        var usage = await Provider().ReadAsync(CancellationToken.None);

        Assert.InRange(usage!.Session!.UsedPct, 0, 100);
        Assert.InRange(usage.Week!.UsedPct, 0, 100);
    }

    [Fact]
    public async Task ReadAsync_MarqueLesDonneesCommeDemo()
    {
        var usage = await Provider().ReadAsync(CancellationToken.None);
        Assert.True(usage!.IsDemo);
    }

    [Fact]
    public async Task Default_NAffichePasDeCoutEnEuros()
    {
        // La maquette affichait « 3,80 € ». La devise est celle de la source et n'est jamais
        // convertie : sans ce test, la recopie de la maquette réintroduit l'euro.
        var usage = await DemoUsageProvider.Default().ReadAsync(CancellationToken.None);

        Assert.DoesNotContain("€", usage!.Cost);
        Assert.StartsWith("$", usage.Cost);
    }

    [Fact]
    public async Task DeuxInstances_NePartagentPasDEtat()
    {
        var a = new DemoUsageProvider("a", "A", "A", "#111",
            new DemoUsageProvider.DemoValues("m", 1, 2, 3, 4, "$1.00", 10, TimeSpan.FromHours(1), 20, TimeSpan.FromDays(1)));
        var b = new DemoUsageProvider("b", "B", "B", "#222",
            new DemoUsageProvider.DemoValues("m", 9, 8, 7, 6, "$2.00", 30, TimeSpan.FromHours(1), 40, TimeSpan.FromDays(1)));

        var ua = await a.ReadAsync(CancellationToken.None);
        var ub = await b.ReadAsync(CancellationToken.None);

        Assert.Equal(1, ua!.SessionTokens);
        Assert.Equal(9, ub!.SessionTokens);
        Assert.Equal("a", ua.ProviderId);
        Assert.Equal("b", ub.ProviderId);
    }
}

public class ClaudeUsageProviderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"claudeprov_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private string ProjectsDir()
    {
        var dir = Path.Combine(_home, ".claude", "projects", "p");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void WriteTranscript(long input, DateTime utc)
    {
        var line = JsonSerializer.Serialize(new
        {
            type = "assistant",
            requestId = "r1",
            timestamp = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            message = new
            {
                id = "m1",
                model = "claude-opus-5",
                role = "assistant",
                usage = new
                {
                    input_tokens = input,
                    output_tokens = 0,
                    cache_creation_input_tokens = 0,
                    cache_read_input_tokens = 0,
                },
            },
        });
        File.WriteAllText(Path.Combine(ProjectsDir(), "a.jsonl"), line);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static HttpClient Failing() => new(new StubHandler(HttpStatusCode.Unauthorized, ""));

    [Fact]
    public void Probe_DossierProjectsAbsent_NonDisponible()
    {
        Directory.CreateDirectory(_home);

        var probe = new ClaudeUsageProvider(_home).Probe();

        Assert.False(probe.Available);
        Assert.Equal("Claude Code", probe.DisplayName);
    }

    [Fact]
    public void Probe_DossierPresentMaisVide_DisponibleAvecPrecision()
    {
        ProjectsDir();

        var probe = new ClaudeUsageProvider(_home).Probe();

        Assert.True(probe.Available);
        Assert.Contains("aucune donnée", probe.Detail);
        Assert.NotEqual("", probe.DataPath);
    }

    [Fact]
    public void Probe_AvecTranscripts_DisponibleSansPrecision()
    {
        WriteTranscript(10, DateTime.UtcNow);

        var probe = new ClaudeUsageProvider(_home).Probe();

        Assert.True(probe.Available);
        Assert.Equal("", probe.Detail);
        Assert.False(probe.IsDemo);
        Assert.False(probe.HiddenByDefault);
    }

    [Fact]
    public async Task ReadAsync_SansAucuneDonnee_RetourneNull()
    {
        ProjectsDir();

        var usage = await new ClaudeUsageProvider(_home, Failing()).ReadAsync(CancellationToken.None);

        Assert.Null(usage);
    }

    [Fact]
    public async Task ReadAsync_AvecTranscripts_DonneLesJetons()
    {
        WriteTranscript(1234, DateTime.UtcNow.AddMinutes(-5));

        var usage = await new ClaudeUsageProvider(_home, Failing()).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(1234, usage!.DayTokens);
        Assert.Equal(1, usage.Requests);
        Assert.Equal("claude-opus-5", usage.Model);
        Assert.False(usage.IsDemo);
    }

    [Fact]
    public async Task ReadAsync_QuotaEnEchec_JaugesAbsentesMaisJetonsPresents()
    {
        // L'endpoint de quota n'est pas documenté : son échec ne doit rien coûter aux métriques.
        WriteTranscript(1234, DateTime.UtcNow.AddMinutes(-5));

        var usage = await new ClaudeUsageProvider(_home, Failing()).ReadAsync(CancellationToken.None);

        Assert.Null(usage!.Session);
        Assert.Null(usage.Week);
        Assert.Equal(1234, usage.DayTokens);
    }

    [Fact]
    public async Task ReadAsync_CredentialsAbsents_PasDAppelReseau()
    {
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        var handler = new ThrowIfCalledHandler();

        var usage = await new ClaudeUsageProvider(_home, new HttpClient(handler)).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.False(handler.WasCalled);
    }

    private sealed class ThrowIfCalledHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task ReadAsync_QuotaDisponible_RemplitLesDeuxJauges()
    {
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        var expires = (long)(DateTime.UtcNow.AddHours(2) - DateTime.UnixEpoch).TotalMilliseconds;
        File.WriteAllText(Path.Combine(_home, ".claude", ".credentials.json"),
            "{\"claudeAiOauth\":{\"accessToken\":\"t\",\"expiresAt\":" + expires + "}}");

        var body = """
        { "five_hour": { "utilization": 62, "resets_at": "2030-01-01T14:00:00Z" },
          "seven_day": { "utilization": 44 } }
        """;
        var http = new HttpClient(new StubHandler(HttpStatusCode.OK, body));

        var usage = await new ClaudeUsageProvider(_home, http).ReadAsync(CancellationToken.None);

        Assert.Equal(62, usage!.Session!.UsedPct);
        Assert.Equal(44, usage.Week!.UsedPct);
    }

    [Fact]
    public void Registre_ContientClaudeEtDemo()
    {
        var ids = UsageProviderRegistry.All.Select(p => p.Id).ToList();

        Assert.Contains("claude", ids);
        Assert.Contains("demo", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
