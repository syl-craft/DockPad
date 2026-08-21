using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class GeminiUsageReaderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"geminihome_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private string Dir(string sub)
    {
        var dir = Path.Combine(_home, ".gemini", "tmp", "abc123", sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Un document de session, dans la forme observée sur une vraie installation.</summary>
    private string WriteSession(string name, DateTime startUtc, params object[] messages)
    {
        var path = Path.Combine(Dir("chats"), name);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            sessionId = "s1",
            projectHash = "abc123",
            startTime = startUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            lastUpdated = startUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            messages,
        }));
        return path;
    }

    private static object Message(string id, DateTime utc, string model,
                                  long input, long cached, long output, long thoughts, long tool) => new
    {
        id,
        type = "gemini",
        model,
        timestamp = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        content = "…",
        tokens = new { input, cached, output, thoughts, tool, total = input + output + thoughts + tool },
    };

    [Fact]
    public void ScanRoot_PointeSurGeminiTmp()
    {
        Assert.Equal(@"C:\Users\Test\.gemini\tmp", GeminiUsageReader.ScanRoot(@"C:\Users\Test"));
    }

    [Fact]
    public void Read_DossierAbsent_RetourneVideSansException()
    {
        Assert.Empty(GeminiUsageReader.Read(Path.Combine(_home, "inexistant"), DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_CompteursMappesEnPreservantLeTotal()
    {
        // input contient déjà cached : le soustraire évite de compter deux fois le même prompt.
        // thoughts est du raisonnement, compté à part par Gemini mais bien de la sortie.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteSession("session-1.json", utc, Message("m1", utc, "gemini-2.5-pro",
            input: 1000, cached: 400, output: 50, thoughts: 20, tool: 10));

        var e = Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));

        Assert.Equal(610, e.Input);        // (1000 − 400) + 10
        Assert.Equal(70, e.Output);        // 50 + 20
        Assert.Equal(400, e.CacheRead);
        Assert.Equal(0, e.CacheWrite);
        Assert.Equal(1080, e.Total);
        Assert.Equal("gemini-2.5-pro", e.Model);
    }

    [Fact]
    public void Read_CacheSuperieurALEntree_NeDonnePasDeNegatif()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteSession("session-1.json", utc, Message("m1", utc, "g", input: 10, cached: 999, output: 0, thoughts: 0, tool: 0));

        var e = Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));

        Assert.Equal(0, e.Input);
    }

    [Fact]
    public void Read_MessageSansJetons_Ignore()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteSession("session-1.json", utc,
            new { id = "u1", type = "user", timestamp = utc.ToString("o", CultureInfo.InvariantCulture), content = "bonjour" },
            Message("m1", utc, "g", 10, 0, 5, 0, 0));

        Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_MemeIdReecrit_GardeLaDerniereValeur()
    {
        // Une réponse en cours est réécrite avec ses jetons définitifs.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteSession("session-1.json", utc,
            Message("m1", utc, "g", 100, 0, 5, 0, 0),
            Message("m1", utc, "g", 100, 0, 90, 0, 0));

        var e = Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(90, e.Output);
    }

    [Fact]
    public void Read_JournauxConsole_NeSontPasLus()
    {
        // ~/.gemini/tmp/<hash>/logs/ contient des .jsonl de trace console et réseau, sans aucune
        // consommation — mesuré 6 Mo sur une machine réelle. Les ouvrir coûterait pour rien.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        var logs = Dir("logs");
        File.WriteAllText(Path.Combine(logs, "session-bruit.jsonl"),
            """{"type":"console","timestamp":"2026-08-20T10:00:00.000Z","payload":{"tokens":{"input":99999}}}""");

        WriteSession("session-1.json", utc, Message("m1", utc, "g", 10, 0, 5, 0, 0));

        var e = Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(10, e.Input);
    }

    [Fact]
    public void Read_FichierModifieAvantLaFenetre_NEstPasLu()
    {
        var utc = DateTime.UtcNow.AddDays(-40);
        var vieux = WriteSession("session-vieille.json", utc, Message("m1", utc, "g", 10, 0, 0, 0, 0));
        File.SetLastWriteTime(vieux, DateTime.Now.AddDays(-40));

        var recent = DateTime.UtcNow.AddMinutes(-5);
        WriteSession("session-recente.json", recent, Message("m2", recent, "g", 5, 0, 0, 0, 0));

        var e = Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(5, e.Input);
    }

    [Fact]
    public void Read_SansHorodatageDeMessage_RetombeSurLeDebutDeSession()
    {
        // Tronqué à la milliseconde : c'est la précision que la fixture écrit, et comparer à la
        // valeur pleine précision échouerait sur les ticks.
        var start = new DateTime(DateTime.UtcNow.AddMinutes(-30).Ticks
                                 / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond,
                                 DateTimeKind.Utc);
        var path = Path.Combine(Dir("chats"), "session-1.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            startTime = start.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            messages = new object[]
            {
                new { id = "m1", type = "gemini", model = "g", tokens = new { input = 10, output = 2 } },
            },
        }));

        var e = Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(start.ToLocalTime(), e.Timestamp);
    }

    [Fact]
    public void Read_DocumentTronque_IgnoreSansException()
    {
        File.WriteAllText(Path.Combine(Dir("chats"), "session-1.json"), """{"messages":[{"id":"m1","tok""");

        Assert.Empty(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_FormeJsonl_EstAcceptee()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        var line = JsonSerializer.Serialize(new
        {
            id = "m1",
            type = "gemini",
            model = "gemini-2.5-pro",
            timestamp = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            tokens = new { input = 200, cached = 50, output = 10, thoughts = 5, tool = 0 },
        });
        File.WriteAllText(Path.Combine(Dir("chats"), "session-2.jsonl"), line);

        var e = Assert.Single(GeminiUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(150, e.Input);
        Assert.Equal(15, e.Output);
        Assert.Equal(50, e.CacheRead);
    }

    // --- Provider

    [Fact]
    public void Probe_DossierAbsent_NonDisponible()
    {
        Directory.CreateDirectory(_home);
        var probe = new GeminiUsageProvider(_home).Probe();

        Assert.False(probe.Available);
        Assert.Equal("Gemini CLI", probe.DisplayName);
    }

    [Fact]
    public void Probe_DossierPresentSansSession_DisponibleAvecPrecision()
    {
        Dir("chats");
        var probe = new GeminiUsageProvider(_home).Probe();

        Assert.True(probe.Available);
        Assert.Contains("aucune donnée", probe.Detail);
    }

    [Fact]
    public async Task ReadAsync_NiQuotaNiCout()
    {
        // Gemini n'expose pas de pourcentage de limite lisible localement, et aucun tarif fiable
        // n'est appliqué : un montant inventé serait pire qu'un tiret.
        var utc = DateTime.UtcNow.AddMinutes(-5);
        WriteSession("session-1.json", utc, Message("m1", utc, "gemini-2.5-pro", 100, 0, 20, 0, 0));

        var usage = await new GeminiUsageProvider(_home).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(120, usage!.DayTokens);
        Assert.Null(usage.Session);
        Assert.Null(usage.Week);
        Assert.Equal("", usage.Cost);
    }

    [Fact]
    public async Task ReadAsync_AucuneSession_RetourneNull()
    {
        Dir("chats");
        Assert.Null(await new GeminiUsageProvider(_home).ReadAsync(CancellationToken.None));
    }
}
