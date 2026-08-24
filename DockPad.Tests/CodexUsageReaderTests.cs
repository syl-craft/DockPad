using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services.Usage;

using DockPad.Services.Localization;

namespace DockPad.Tests;

public class CodexUsageReaderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"codexhome_{Guid.NewGuid():N}");
    private readonly string? _savedHomeVariable = Environment.GetEnvironmentVariable(CodexUsageReader.HomeVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, _savedHomeVariable);
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private string Dir(string root)
    {
        var dir = Path.Combine(_home, ".codex", root, "2026", "08");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Une ligne token_count telle que Codex les écrit dans un rollout.</summary>
    private static string TokenCountLine(DateTime utc, string model,
                                         long input, long cached, long output) =>
        JsonSerializer.Serialize(new
        {
            type = "event_msg",
            timestamp = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            payload = new
            {
                type = "token_count",
                info = new
                {
                    model,
                    last_token_usage = new
                    {
                        input_tokens = input,
                        cached_input_tokens = cached,
                        output_tokens = output,
                        reasoning_output_tokens = 0,
                    },
                },
            },
        });

    private string WriteRollout(string root, string name, params string[] lines)
    {
        var path = Path.Combine(Dir(root), name);
        File.WriteAllLines(path, lines);
        return path;
    }

    // --- ScanRoots

    [Fact]
    public void ScanRoots_ContientLesSessionsEtLesArchives()
    {
        var roots = CodexUsageReader.ScanRoots(@"C:\Users\Test");

        // Codex déplace un rollout de sessions vers archived_sessions : ce n'est pas une autre
        // consommation mais le même fichier qui bouge. N'en lire qu'une le ferait disparaître.
        Assert.Contains(@"C:\Users\Test\.codex\sessions", roots);
        Assert.Contains(@"C:\Users\Test\.codex\archived_sessions", roots);
    }

    [Fact]
    public void ScanRoots_RespecteLaVariableDEnvironnement()
    {
        Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, @"D:\ailleurs\codex");
        try
        {
            var roots = CodexUsageReader.ScanRoots(@"C:\Users\Test");
            Assert.Contains(@"D:\ailleurs\codex\sessions", roots);
            Assert.DoesNotContain(@"C:\Users\Test\.codex\sessions", roots);
        }
        finally { Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, null); }
    }

    // --- Read

    [Fact]
    public void Read_DossierAbsent_RetourneVideSansException()
    {
        Assert.Empty(CodexUsageReader.Read(Path.Combine(_home, "inexistant"), DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_CompteursMappes_SansCompterLeCacheDeuxFois()
    {
        // input_tokens est le prompt entier, cached_input_tokens en est un sous-ensemble.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteRollout("sessions", "rollout-1.jsonl", TokenCountLine(utc, "gpt-5-codex", 1000, 400, 60));

        var e = Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));

        Assert.Equal(600, e.Input);
        Assert.Equal(60, e.Output);
        Assert.Equal(400, e.CacheRead);
        Assert.Equal(1060, e.Total);
        Assert.Equal("gpt-5-codex", e.Model);
    }

    [Fact]
    public void Read_LesDeuxRacinesSontLues()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteRollout("sessions", "rollout-1.jsonl", TokenCountLine(utc, "m", 100, 0, 0));
        WriteRollout("archived_sessions", "rollout-2.jsonl", TokenCountLine(utc, "m", 50, 0, 0));

        Assert.Equal(2, CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)).Count);
    }

    [Fact]
    public void Read_LignesSansTokenCount_Ignorees()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteRollout("sessions", "rollout-1.jsonl",
            """{"type":"session_meta","payload":{"id":"s1"}}""",
            """{"type":"response_item","payload":{"type":"message","content":"bonjour"}}""",
            TokenCountLine(utc, "m", 10, 0, 5));

        Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_LigneTronquee_IgnoreeSansException()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        var path = Path.Combine(Dir("sessions"), "rollout-1.jsonl");
        File.WriteAllText(path,
            TokenCountLine(utc, "m", 10, 0, 5) + "\n" +
            """{"type":"event_msg","payload":{"type":"token_count","in""");

        Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_FichierModifieAvantLaFenetre_NEstPasLu()
    {
        var vieux = WriteRollout("sessions", "rollout-vieux.jsonl",
            TokenCountLine(DateTime.UtcNow.AddDays(-40), "m", 10, 0, 0));
        File.SetLastWriteTime(vieux, DateTime.Now.AddDays(-40));

        WriteRollout("sessions", "rollout-recent.jsonl",
            TokenCountLine(DateTime.UtcNow.AddMinutes(-5), "m", 5, 0, 0));

        var e = Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(5, e.Input);
    }

    [Fact]
    public void Read_FichierQuiNEstPasUnRollout_Ignore()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        File.WriteAllText(Path.Combine(Dir("sessions"), "autre-chose.jsonl"), TokenCountLine(utc, "m", 999, 0, 0));
        WriteRollout("sessions", "rollout-1.jsonl", TokenCountLine(utc, "m", 10, 0, 0));

        var e = Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(10, e.Input);
    }

    [Fact]
    public void Read_PlusieursTours_ChacunCompte()
    {
        // last_token_usage est un delta de tour : les événements s'additionnent, ils ne se
        // remplacent pas.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteRollout("sessions", "rollout-1.jsonl",
            TokenCountLine(utc, "m", 100, 0, 10),
            TokenCountLine(utc.AddMinutes(1), "m", 200, 0, 20));

        var entries = CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1));

        Assert.Equal(2, entries.Count);
        Assert.Equal(330, entries.Sum(e => e.Total));
    }

    // --- Provider

    [Fact]
    public void Probe_DossierAbsent_NonDisponible()
    {
        Directory.CreateDirectory(_home);
        var probe = new CodexUsageProvider(_home).Probe();

        Assert.False(probe.Available);
        Assert.Equal("Codex", probe.DisplayName);
    }

    [Fact]
    public void Probe_DossierPresentSansRollout_DisponibleAvecPrecision()
    {
        Dir("sessions");
        var probe = new CodexUsageProvider(_home).Probe();

        Assert.True(probe.Available);
        // Le libellé est traduit : le comparer en dur ferait échouer le test selon la langue
        // courante du processus, ce qui est précisément le défaut qu'on veut éviter.
        Assert.Equal(Loc.T("Probe_NoSessionData"), probe.Detail);
    }

    [Fact]
    public async Task ReadAsync_NiQuotaNiCout()
    {
        // Le quota Codex existe, mais il faut lancer « codex app-server --stdio » en JSON-RPC :
        // un processus enfant chaque minute pour deux nombres, hors de proportion ici.
        WriteRollout("sessions", "rollout-1.jsonl",
            TokenCountLine(DateTime.UtcNow.AddMinutes(-5), "gpt-5-codex", 100, 0, 20));

        var usage = await new CodexUsageProvider(_home).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(120, usage!.DayTokens);
        Assert.Null(usage.Session);
        Assert.Null(usage.Week);
        Assert.Equal("", usage.Cost);
    }

    [Fact]
    public async Task ReadAsync_InstalleSansRollout_DonneUnInstantaneAZero()
    {
        // Détecté mais inactif : onglet conservé, valeurs à zéro.
        Dir("sessions");

        var usage = await new CodexUsageProvider(_home).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(0, usage!.MonthTokens);
        Assert.Equal(0, usage.Requests);
    }

    [Fact]
    public async Task ReadAsync_NonInstalle_RetourneNull()
    {
        // La variable d'environnement est neutralisée : sinon un CODEX_HOME réel sur la machine de
        // développement ferait passer le test pour une mauvaise raison.
        Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, null);

        Assert.Null(await new CodexUsageProvider(_home).ReadAsync(CancellationToken.None));
    }
}
