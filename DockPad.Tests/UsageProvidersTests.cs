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
    public async Task ReadAsync_InstalleSansDonnee_DonneUnInstantaneAZero()
    {
        // Détecté mais inactif sur la période : le fournisseur garde son onglet, à zéro. Disparaître
        // du bandeau doit vouloir dire « pas installé », et rien d'autre — sinon un outil installé
        // qu'on n'a pas utilisé ce mois-ci semble ne pas être détecté du tout.
        ProjectsDir();

        var usage = await new ClaudeUsageProvider(_home, Failing()).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(0, usage!.MonthTokens);
        Assert.Equal(0, usage.Requests);
        Assert.Equal("", usage.Model);
    }

    [Fact]
    public async Task ReadAsync_NonInstalle_RetourneNull()
    {
        // Aucun dossier : rien à afficher, pas même un onglet vide.
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

    /// <summary>Compte les appels sortants, pour vérifier la cadence.</summary>
    private sealed class CountingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private void WriteCredentials()
    {
        var expires = (long)(DateTime.UtcNow.AddHours(2) - DateTime.UnixEpoch).TotalMilliseconds;
        Directory.CreateDirectory(Path.Combine(_home, ".claude"));
        File.WriteAllText(Path.Combine(_home, ".claude", ".credentials.json"),
            "{\"claudeAiOauth\":{\"accessToken\":\"t\",\"expiresAt\":" + expires + "}}");
    }

    private const string QuotaBody = """
    { "five_hour": { "utilization": 62 }, "seven_day": { "utilization": 44 } }
    """;

    [Fact]
    public async Task ReadAsync_QuotaInterrogeAuPlusUneFoisParIntervalle()
    {
        // Le bandeau se rafraîchit chaque minute ; interroger le quota à cette cadence a valu des
        // HTTP 429 en usage réel, et les jauges disparaissaient à cause de notre propre insistance.
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();

        var handler = new CountingHandler(HttpStatusCode.OK, QuotaBody);
        var now = new DateTime(2026, 8, 21, 14, 0, 0);
        var provider = new ClaudeUsageProvider(_home, new HttpClient(handler), () => now);

        await provider.ReadAsync(CancellationToken.None);
        await provider.ReadAsync(CancellationToken.None);
        await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ReadAsync_EntreDeuxAppels_LesJaugesGardentLaDerniereValeur()
    {
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();

        var handler = new CountingHandler(HttpStatusCode.OK, QuotaBody);
        var now = new DateTime(2026, 8, 21, 14, 0, 0);
        var provider = new ClaudeUsageProvider(_home, new HttpClient(handler), () => now);

        await provider.ReadAsync(CancellationToken.None);
        var second = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(62, second!.Session!.UsedPct);
    }

    /// <summary>Répond une fois par entrée, puis répète la dernière.</summary>
    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = responses[Math.Min(_index++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Body),
            });
        }
    }

    [Fact]
    public async Task ReadAsync_EchecPassagerPuisPerime_DegradeParPaliers()
    {
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();

        var handler = new SequenceHandler(
            (HttpStatusCode.OK, QuotaBody),
            (HttpStatusCode.TooManyRequests, ""));
        var clock = new[] { new DateTime(2026, 8, 21, 14, 0, 0) };
        var provider = new ClaudeUsageProvider(_home, new HttpClient(handler), () => clock[0]);

        var premier = await provider.ReadAsync(CancellationToken.None);
        Assert.Equal(62, premier!.Session!.UsedPct);

        // Au-delà de l'intervalle : nouvel appel, qui échoue en 429. Un échec passager ne doit pas
        // faire disparaître des jauges qui étaient justes il y a six minutes.
        clock[0] = clock[0].AddMinutes(6);
        var second = await provider.ReadAsync(CancellationToken.None);
        Assert.Equal(62, second!.Session!.UsedPct);

        // Au-delà de l'âge maximal : on préfère ne rien affirmer plutôt qu'un chiffre périmé.
        clock[0] = clock[0].AddMinutes(20);
        var troisieme = await provider.ReadAsync(CancellationToken.None);
        Assert.Null(troisieme!.Session);
        Assert.Equal(10, troisieme.DayTokens);   // les jetons, eux, restent
    }

    /// <summary>Annule le premier appel, puis répond normalement.</summary>
    private sealed class CancelFirstCallHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (++Calls == 1) throw new OperationCanceledException();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task ReadAsync_QuotaRefuse_LInstantanePorteUneNotice()
    {
        // Des jauges qui disparaissent sans un mot, c'est un bandeau qui ne dit pas ce qu'il se
        // passe : la raison ne doit pas vivre seulement dans le fichier de log.
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();
        var now = new DateTime(2026, 8, 21, 14, 0, 0);
        var http = new HttpClient(new StubHandler(HttpStatusCode.TooManyRequests, ""));

        var usage = await new ClaudeUsageProvider(_home, http, () => now).ReadAsync(CancellationToken.None);

        Assert.Null(usage!.Session);
        Assert.Contains("indisponible", usage.QuotaNotice);
        Assert.Contains("429", usage.QuotaNoticeNote);
    }

    [Fact]
    public async Task ReadAsync_QuotaDisponible_AucuneNotice()
    {
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();
        var http = new HttpClient(new StubHandler(HttpStatusCode.OK, QuotaBody));

        var usage = await new ClaudeUsageProvider(_home, http).ReadAsync(CancellationToken.None);

        Assert.Equal("", usage!.QuotaNotice);
        Assert.Equal("", usage.QuotaNoticeNote);
    }

    [Fact]
    public async Task ReadAsync_QuotaRefuse_LaNoticeAnnonceLaProchaineTentative()
    {
        // Le silence complet laisse croire à une panne définitive : l'utilisateur doit savoir que
        // l'application va réessayer, et quand.
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();
        var clock = new[] { new DateTime(2026, 8, 21, 14, 0, 0) };
        var http = new HttpClient(new StubHandler(HttpStatusCode.TooManyRequests, ""));
        var provider = new ClaudeUsageProvider(_home, http, () => clock[0]);

        await provider.ReadAsync(CancellationToken.None);
        clock[0] = clock[0].AddMinutes(1);
        var usage = await provider.ReadAsync(CancellationToken.None);

        Assert.Contains("4 min", usage!.QuotaNotice);
    }

    [Fact]
    public async Task ReadAsync_JetonExpire_LaNoticeLeDit()
    {
        // Chemin muet : l'ancienne version sortait sans journal ni notice, et les jauges vides
        // n'avaient aucune explication nulle part.
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        var expire = (long)(DateTime.UtcNow.AddHours(-1) - DateTime.UnixEpoch).TotalMilliseconds;
        Directory.CreateDirectory(Path.Combine(_home, ".claude"));
        File.WriteAllText(Path.Combine(_home, ".claude", ".credentials.json"),
            "{\"claudeAiOauth\":{\"accessToken\":\"t\",\"expiresAt\":" + expire + "}}");

        var usage = await new ClaudeUsageProvider(_home, Failing()).ReadAsync(CancellationToken.None);

        Assert.Contains("indisponible", usage!.QuotaNotice);
        Assert.Contains("jeton", usage.QuotaNoticeNote);
    }

    [Fact]
    public async Task ReadAsync_CredentialsAbsents_LaNoticeLeDit()
    {
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));

        var usage = await new ClaudeUsageProvider(_home, Failing()).ReadAsync(CancellationToken.None);

        Assert.Contains("indisponible", usage!.QuotaNotice);
        Assert.Contains("credentials", usage.QuotaNoticeNote);
    }

    [Fact]
    public async Task ReadAsync_LectureAnnulee_NeConsommePasLeCreneau()
    {
        // Le créneau de cinq minutes est posé avant l'appel : une annulation — masquer puis
        // réafficher la fenêtre le fait — le brûlait sans rien tenter et sans rien journaliser,
        // donc cinq minutes de jauges vides inexpliquées.
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();
        var now = new DateTime(2026, 8, 21, 14, 0, 0);
        var handler = new CancelFirstCallHandler(QuotaBody);
        var provider = new ClaudeUsageProvider(_home, new HttpClient(handler), () => now);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ReadAsync(CancellationToken.None));
        var usage = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(62, usage!.Session!.UsedPct);
    }

    [Fact]
    public async Task ReadAsync_CauseQuiChange_LaNoticeSuit()
    {
        WriteTranscript(10, DateTime.UtcNow.AddMinutes(-5));
        WriteCredentials();
        var clock = new[] { new DateTime(2026, 8, 21, 14, 0, 0) };
        var handler = new SequenceHandler(
            (HttpStatusCode.TooManyRequests, ""),
            (HttpStatusCode.OK, "pas du json"));
        var provider = new ClaudeUsageProvider(_home, new HttpClient(handler), () => clock[0]);

        var premier = await provider.ReadAsync(CancellationToken.None);
        Assert.Contains("429", premier!.QuotaNoticeNote);

        clock[0] = clock[0].AddMinutes(6);
        var second = await provider.ReadAsync(CancellationToken.None);

        Assert.DoesNotContain("429", second!.QuotaNoticeNote);
        Assert.Contains("forme de réponse", second.QuotaNoticeNote);
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
