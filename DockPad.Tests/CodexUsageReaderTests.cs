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

    /// <summary>
    /// Une ligne token_count telle que Codex les écrit dans un rollout — <b>sans</b> modèle : il
    /// n'y en a pas, il vit dans le <c>turn_context</c> du tour.
    /// </summary>
    private static string TokenCountLine(DateTime utc, long input, long cached, long output) =>
        JsonSerializer.Serialize(new
        {
            type = "event_msg",
            timestamp = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            payload = new
            {
                type = "token_count",
                info = new
                {
                    model_context_window = 258400,
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

    /// <summary>L'ouverture d'un tour, qui porte le modèle effectivement utilisé.</summary>
    private static string TurnContextLine(DateTime utc, string model) =>
        JsonSerializer.Serialize(new
        {
            timestamp = utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            type = "turn_context",
            payload = new { turn_id = Guid.NewGuid().ToString(), cwd = @"C:\dev", model, effort = "medium" },
        });

    private void WriteConfig(string content)
    {
        Directory.CreateDirectory(Path.Combine(_home, ".codex"));
        File.WriteAllText(Path.Combine(_home, ".codex", "config.toml"), content);
    }

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
        WriteRollout("sessions", "rollout-1.jsonl",
            TurnContextLine(utc, "gpt-5-codex"),
            TokenCountLine(utc, 1000, 400, 60));

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
        WriteRollout("sessions", "rollout-1.jsonl", TokenCountLine(utc, 100, 0, 0));
        WriteRollout("archived_sessions", "rollout-2.jsonl", TokenCountLine(utc, 50, 0, 0));

        Assert.Equal(2, CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)).Count);
    }

    [Fact]
    public void Read_LignesSansTokenCount_Ignorees()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteRollout("sessions", "rollout-1.jsonl",
            """{"type":"session_meta","payload":{"id":"s1"}}""",
            """{"type":"response_item","payload":{"type":"message","content":"bonjour"}}""",
            TokenCountLine(utc, 10, 0, 5));

        Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_LigneTronquee_IgnoreeSansException()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        var path = Path.Combine(Dir("sessions"), "rollout-1.jsonl");
        File.WriteAllText(path,
            TokenCountLine(utc, 10, 0, 5) + "\n" +
            """{"type":"event_msg","payload":{"type":"token_count","in""");

        Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_FichierModifieAvantLaFenetre_NEstPasLu()
    {
        var vieux = WriteRollout("sessions", "rollout-vieux.jsonl",
            TokenCountLine(DateTime.UtcNow.AddDays(-40), 10, 0, 0));
        File.SetLastWriteTime(vieux, DateTime.Now.AddDays(-40));

        WriteRollout("sessions", "rollout-recent.jsonl",
            TokenCountLine(DateTime.UtcNow.AddMinutes(-5), 5, 0, 0));

        var e = Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(5, e.Input);
    }

    [Fact]
    public void Read_FichierQuiNEstPasUnRollout_Ignore()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        File.WriteAllText(Path.Combine(Dir("sessions"), "autre-chose.jsonl"), TokenCountLine(utc, 999, 0, 0));
        WriteRollout("sessions", "rollout-1.jsonl", TokenCountLine(utc, 10, 0, 0));

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
            TokenCountLine(utc, 100, 0, 10),
            TokenCountLine(utc.AddMinutes(1), 200, 0, 20));

        var entries = CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1));

        Assert.Equal(2, entries.Count);
        Assert.Equal(330, entries.Sum(e => e.Total));
    }

    [Fact]
    public void Read_ChaqueReleveDeJetonsPrendLeModeleDuTourQuiLePrecede()
    {
        // Un changement de modèle en cours de session (/model) ouvre un tour avec le nouveau.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteRollout("sessions", "rollout-1.jsonl",
            TurnContextLine(utc, "gpt-6-sol"),
            TokenCountLine(utc, 100, 0, 10),
            TurnContextLine(utc.AddMinutes(1), "gpt-6-astra"),
            TokenCountLine(utc.AddMinutes(1), 200, 0, 20));

        var entries = CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1));

        Assert.Equal(["gpt-6-sol", "gpt-6-astra"], entries.Select(e => e.Model));
    }

    [Fact]
    public void Read_TourOuvertAvantLaFenetre_DonneQuandMemeSonModele()
    {
        // Le tour a commencé hier, son relevé tombe aujourd'hui : la borne filtre les jetons, pas
        // ce qu'on sait du tour.
        var now = DateTime.UtcNow;
        WriteRollout("sessions", "rollout-1.jsonl",
            TurnContextLine(now.AddDays(-2), "gpt-6-sol"),
            TokenCountLine(now.AddMinutes(-5), 10, 0, 0));

        var e = Assert.Single(CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal("gpt-6-sol", e.Model);
    }

    [Fact]
    public void Read_LeModeleNeTraversePasLesFichiers()
    {
        // Deux sessions distinctes : un relevé sans tour connu reste sans modèle plutôt que
        // d'hériter de celui d'une autre session.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteRollout("sessions", "rollout-1.jsonl", TurnContextLine(utc, "gpt-6-sol"), TokenCountLine(utc, 10, 0, 0));
        WriteRollout("sessions", "rollout-2.jsonl", TokenCountLine(utc, 20, 0, 0));

        var entries = CodexUsageReader.Read(_home, DateTime.Now.AddDays(-1));
        Assert.Equal("", entries.Single(e => e.Input == 20).Model);
    }

    // --- ConfiguredModel

    [Fact]
    public void ConfiguredModel_LitLaCleDeTete()
    {
        WriteConfig("""
            # commentaire
            model = "gpt-6-astra"
            model_reasoning_effort = "medium"

            [tui]
            model = "autre"
            """);

        Assert.Equal("gpt-6-astra", CodexUsageReader.ConfiguredModel(_home));
    }

    [Fact]
    public void ConfiguredModel_CleSeulementDansUneSection_Ignoree()
    {
        // Seule la table racine dit le modèle par défaut : un « model » de profil ou de section ne
        // vaut que là où il est.
        WriteConfig("""
            [profiles.rapide]
            model = "gpt-6-sol"
            """);

        Assert.Equal("", CodexUsageReader.ConfiguredModel(_home));
    }

    [Fact]
    public void ConfiguredModel_ApostrophesEtCommentaireEnFinDeLigne()
    {
        WriteConfig("model = 'gpt-6-sol'   # le rapide\n");

        Assert.Equal("gpt-6-sol", CodexUsageReader.ConfiguredModel(_home));
    }

    [Fact]
    public void ConfiguredModel_FichierAbsent_Vide()
    {
        Assert.Equal("", CodexUsageReader.ConfiguredModel(Path.Combine(_home, "inexistant")));
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
    public async Task ReadAsync_SansReleveDeQuota_GardeLesJetonsEtExpliqueLesJaugesAbsentes()
    {
        // Un relevé de jetons sans rate_limits ne permet pas de calculer un pourcentage de quota.
        WriteRollout("sessions", "rollout-1.jsonl",
            TokenCountLine(DateTime.UtcNow.AddMinutes(-5), 100, 0, 20));

        var usage = await new CodexUsageProvider(_home).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(120, usage!.DayTokens);
        Assert.Null(usage.Session);
        Assert.Null(usage.Week);
        Assert.Equal("", usage.Cost);
        Assert.NotEmpty(usage.QuotaNotice);
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
    public async Task ReadAsync_SansActivite_AfficheLeModeleConfigure()
    {
        Dir("sessions");
        WriteConfig("model = \"gpt-6-astra\"\n");

        var usage = await new CodexUsageProvider(_home).ReadAsync(CancellationToken.None);

        Assert.Equal("gpt-6-astra", usage!.Model);
    }

    [Fact]
    public async Task ReadAsync_LeModeleUtiliseGagneSurLeModeleConfigure()
    {
        // La configuration dit ce qu'on démarrerait ; un --model ou un /model dit ce qu'on a fait.
        WriteConfig("model = \"gpt-6-astra\"\n");
        var utc = DateTime.UtcNow.AddMinutes(-5);
        WriteRollout("sessions", "rollout-1.jsonl", TurnContextLine(utc, "gpt-6-sol"), TokenCountLine(utc, 10, 0, 0));

        var usage = await new CodexUsageProvider(_home).ReadAsync(CancellationToken.None);

        Assert.Equal("gpt-6-sol", usage!.Model);
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
