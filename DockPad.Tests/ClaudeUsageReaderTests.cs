using System.Globalization;
using System.IO;
using System.Text.Json;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class ClaudeUsageReaderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"claudehome_{Guid.NewGuid():N}");

    private string ProjectsDir
    {
        get
        {
            var dir = Path.Combine(_home, ".claude", "projects", "un-projet");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    /// <summary>
    /// Une ligne « assistant » telle que Claude Code les écrit : timestamp UTC suffixé Z, usage à
    /// quatre compteurs sous <c>message</c>. Sérialisée plutôt qu'écrite à la main — un JSON de
    /// fixture invalide ferait passer un test de parsing tolérant pour une réussite.
    /// </summary>
    private static string AssistantLine(string messageId, string requestId, DateTime utc, string model,
                                        long input, long output, long cacheWrite, long cacheRead) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            requestId,
            timestamp = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            message = new
            {
                id = messageId,
                model,
                role = "assistant",
                usage = new
                {
                    input_tokens = input,
                    output_tokens = output,
                    cache_creation_input_tokens = cacheWrite,
                    cache_read_input_tokens = cacheRead,
                },
            },
        });

    private string WriteFile(string name, params string[] lines)
    {
        var path = Path.Combine(ProjectsDir, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    // --- ScanRoots

    [Fact]
    public void ScanRoots_ContientLesDeuxEmplacementsConnus()
    {
        var roots = ClaudeUsageReader.ScanRoots(@"C:\Users\Test");

        Assert.Contains(@"C:\Users\Test\.claude\projects", roots);
        Assert.Contains(@"C:\Users\Test\.config\claude\projects", roots);
    }

    [Fact]
    public void ScanRoots_NeDupliquePas()
    {
        var roots = ClaudeUsageReader.ScanRoots(@"C:\Users\Test");
        Assert.Equal(roots.Distinct(StringComparer.OrdinalIgnoreCase).Count(), roots.Count);
    }

    // --- Read

    [Fact]
    public void Read_DossierAbsent_RetourneVideSansException()
    {
        var entries = ClaudeUsageReader.Read(Path.Combine(_home, "inexistant"), DateTime.Now.AddDays(-1));
        Assert.Empty(entries);
    }

    [Fact]
    public void Read_AdditionneLesQuatreCompteurs()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteFile("a.jsonl", AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 20, 30, 40));

        var entries = ClaudeUsageReader.Read(_home, DateTime.Now.AddDays(-1));

        var e = Assert.Single(entries);
        Assert.Equal(10, e.Input);
        Assert.Equal(20, e.Output);
        Assert.Equal(30, e.CacheWrite);
        Assert.Equal(40, e.CacheRead);
        Assert.Equal(100, e.Total);
        Assert.Equal("claude-opus-5", e.Model);
    }

    [Fact]
    public void Read_MemeMessageDansDeuxFichiers_CompteUneSeuleFois()
    {
        // Reprise de session et sidechains réécrivent le même message ailleurs. Sans déduplication
        // sur (message.id, requestId), les totaux gonflent silencieusement.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        var line = AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 20, 0, 0);
        WriteFile("a.jsonl", line);
        WriteFile("b.jsonl", line);

        var entries = ClaudeUsageReader.Read(_home, DateTime.Now.AddDays(-1));

        Assert.Single(entries);
    }

    [Fact]
    public void Read_MemeIdMaisRequeteDifferente_CompteDeuxFois()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteFile("a.jsonl",
            AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 0, 0, 0),
            AssistantLine("m1", "r2", utc, "claude-opus-5", 10, 0, 0, 0));

        Assert.Equal(2, ClaudeUsageReader.Read(_home, DateTime.Now.AddDays(-1)).Count);
    }

    [Fact]
    public void Read_LignesNonAssistant_Ignorees()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteFile("a.jsonl",
            """{"type":"user","message":{"role":"user","content":"bonjour"}}""",
            """{"type":"summary","summary":"…"}""",
            AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 0, 0, 0));

        Assert.Single(ClaudeUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_DerniereLigneTronquee_IgnoreeSansException()
    {
        // Claude Code écrit pendant qu'on lit : la dernière ligne peut être un JSON incomplet.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        var path = Path.Combine(ProjectsDir, "a.jsonl");
        File.WriteAllText(path,
            AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 0, 0, 0) + "\n" +
            """{"type":"assistant","message":{"id":"m2","usa""");

        Assert.Single(ClaudeUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_LigneSansUsage_Ignoree()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteFile("a.jsonl",
            """{"type":"assistant","requestId":"r0","message":{"id":"m0","model":"claude-opus-5","role":"assistant"}}""",
            AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 0, 0, 0));

        Assert.Single(ClaudeUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_FichierModifieAvantLaFenetre_NEstPasLu()
    {
        var utc = DateTime.UtcNow.AddDays(-40);
        var vieux = WriteFile("vieux.jsonl", AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 0, 0, 0));
        File.SetLastWriteTime(vieux, DateTime.Now.AddDays(-40));

        var recent = DateTime.UtcNow.AddMinutes(-5);
        WriteFile("recent.jsonl", AssistantLine("m2", "r2", recent, "claude-opus-5", 5, 0, 0, 0));

        var entries = ClaudeUsageReader.Read(_home, DateTime.Now.AddDays(-1));

        var e = Assert.Single(entries);
        Assert.Equal(5, e.Input);
    }

    [Fact]
    public void Read_TimestampUtc_ConvertiEnHeureLocale()
    {
        var utc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        WriteFile("a.jsonl", AssistantLine("m1", "r1", utc, "claude-opus-5", 10, 0, 0, 0));
        File.SetLastWriteTime(Path.Combine(ProjectsDir, "a.jsonl"), DateTime.Now);

        var e = Assert.Single(ClaudeUsageReader.Read(_home, new DateTime(2026, 8, 1)));

        Assert.Equal(utc.ToLocalTime(), e.Timestamp);
        Assert.Equal(DateTimeKind.Local, e.Timestamp.Kind);
    }

    // --- Aggregate

    private static ClaudeUsageReader.UsageEntry Entry(DateTime local, long tokens, string model = "claude-opus-5") =>
        new($"k{local.Ticks}{tokens}", local, model, tokens, 0, 0, 0);

    [Fact]
    public void Aggregate_JourEtMois_SurLesBornesLocales()
    {
        var now = new DateTime(2026, 8, 20, 14, 0, 0);
        var entries = new[]
        {
            Entry(now.AddHours(-1),  100),   // aujourd'hui
            Entry(now.AddDays(-2),   200),   // ce mois, pas aujourd'hui
            Entry(now.AddMonths(-1), 400),   // mois précédent
        };

        var t = ClaudeUsageReader.Aggregate(entries, now);

        Assert.Equal(100, t.Day);
        Assert.Equal(300, t.Month);
    }

    [Fact]
    public void Aggregate_MinuitPasse_NeComptePasHier()
    {
        var now = new DateTime(2026, 8, 20, 0, 30, 0);
        var entries = new[] { Entry(now.AddHours(-1), 100) };   // 23h30 la veille

        Assert.Equal(0, ClaudeUsageReader.Aggregate(entries, now).Day);
    }

    [Fact]
    public void Aggregate_BlocDeCinqHeures_AncreSurLaPremiereActivite()
    {
        // Le bloc démarre à la première activité non couverte et dure 5 h. Une coupure de plus de
        // 5 h ouvre un nouveau bloc : seul celui qui contient « maintenant » est le bloc actif.
        var now = new DateTime(2026, 8, 20, 14, 0, 0);
        var entries = new[]
        {
            Entry(now.AddHours(-7), 100),   // bloc expiré [-7h, -2h)
            Entry(now.AddHours(-1), 50),    // bloc actif  [-1h, +4h)
        };

        var t = ClaudeUsageReader.Aggregate(entries, now);

        Assert.Equal(50, t.Session);
    }

    [Fact]
    public void Aggregate_ToutDansUnBlocExpire_SessionNulle()
    {
        var now = new DateTime(2026, 8, 20, 14, 0, 0);
        var entries = new[] { Entry(now.AddHours(-6), 100), Entry(now.AddHours(-4), 30) };

        Assert.Equal(0, ClaudeUsageReader.Aggregate(entries, now).Session);
    }

    [Fact]
    public void Aggregate_PlusieursEntreesDansLeBlocActif_Additionnees()
    {
        var now = new DateTime(2026, 8, 20, 14, 0, 0);
        var entries = new[] { Entry(now.AddHours(-3), 100), Entry(now.AddHours(-1), 30) };

        Assert.Equal(130, ClaudeUsageReader.Aggregate(entries, now).Session);
    }

    [Fact]
    public void Aggregate_Requetes_CompteLesEntreesDuJour()
    {
        var now = new DateTime(2026, 8, 20, 14, 0, 0);
        var entries = new[]
        {
            Entry(now.AddHours(-1), 10),
            Entry(now.AddHours(-2), 10),
            Entry(now.AddDays(-3),  10),
        };

        Assert.Equal(2, ClaudeUsageReader.Aggregate(entries, now).Requests);
    }

    [Fact]
    public void Aggregate_Modele_CeluiDeLEntreeLaPlusRecente()
    {
        var now = new DateTime(2026, 8, 20, 14, 0, 0);
        var entries = new[]
        {
            Entry(now.AddHours(-5), 10, "claude-sonnet-5"),
            Entry(now.AddHours(-1), 10, "claude-opus-5"),
        };

        Assert.Equal("claude-opus-5", ClaudeUsageReader.Aggregate(entries, now).Model);
    }

    [Fact]
    public void Aggregate_AucuneEntree_TousLesTotauxNuls()
    {
        var t = ClaudeUsageReader.Aggregate([], new DateTime(2026, 8, 20, 14, 0, 0));

        Assert.Equal(0, t.Session);
        Assert.Equal(0, t.Day);
        Assert.Equal(0, t.Month);
        Assert.Equal(0, t.Requests);
        Assert.Equal("", t.Model);
        Assert.Equal(0m, t.Cost);
    }

    [Fact]
    public void Aggregate_Cout_PorteSurLeMoisEnCours()
    {
        var now = new DateTime(2026, 8, 20, 14, 0, 0);
        var entries = new[]
        {
            Entry(now.AddDays(-2),   1_000_000),   // ce mois
            Entry(now.AddMonths(-1), 1_000_000),   // mois précédent, hors coût
        };

        var t = ClaudeUsageReader.Aggregate(entries, now);

        Assert.Equal(ClaudePricing.Cost("claude-opus-5", 1_000_000, 0, 0, 0), t.Cost);
    }
}
