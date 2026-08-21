using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services.Usage;
using Microsoft.Data.Sqlite;

namespace DockPad.Tests;

public class CopilotUsageReaderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"copilothome_{Guid.NewGuid():N}");
    private readonly string? _savedHomeVariable = Environment.GetEnvironmentVariable(CopilotUsageReader.HomeVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CopilotUsageReader.HomeVariable, _savedHomeVariable);
        SqliteConnection.ClearAllPools();   // sinon le fichier reste verrouillé et la suppression échoue
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    /// <summary>Crée la base avec le schéma observé, et y insère les lignes demandées.</summary>
    private string WriteDatabase(params (long Id, string Model, long Input, long Output,
                                         long CacheRead, long CacheWrite, DateTime CreatedUtc)[] rows)
    {
        var path = CopilotUsageReader.DatabasePath(_home);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
            CREATE TABLE assistant_usage_events (
                id INTEGER PRIMARY KEY,
                model TEXT,
                input_tokens INTEGER,
                output_tokens INTEGER,
                cache_read_tokens INTEGER,
                cache_write_tokens INTEGER,
                created_at TEXT
            )
            """;
            create.ExecuteNonQuery();
        }

        foreach (var row in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
            INSERT INTO assistant_usage_events
                (id, model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, created_at)
            VALUES ($id, $model, $in, $out, $cr, $cw, $at)
            """;
            insert.Parameters.AddWithValue("$id", row.Id);
            insert.Parameters.AddWithValue("$model", row.Model);
            insert.Parameters.AddWithValue("$in", row.Input);
            insert.Parameters.AddWithValue("$out", row.Output);
            insert.Parameters.AddWithValue("$cr", row.CacheRead);
            insert.Parameters.AddWithValue("$cw", row.CacheWrite);
            insert.Parameters.AddWithValue("$at",
                row.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            insert.ExecuteNonQuery();
        }

        return path;
    }

    // --- DatabasePath

    [Fact]
    public void DatabasePath_ParDefautSousLeDossierCopilot()
    {
        Assert.Equal(@"C:\Users\Test\.copilot\session-store.db",
                     CopilotUsageReader.DatabasePath(@"C:\Users\Test"));
    }

    [Fact]
    public void DatabasePath_RespecteLaVariableDEnvironnement()
    {
        Environment.SetEnvironmentVariable(CopilotUsageReader.HomeVariable, @"D:\ailleurs\copilot");
        try
        {
            Assert.Equal(@"D:\ailleurs\copilot\session-store.db",
                         CopilotUsageReader.DatabasePath(@"C:\Users\Test"));
        }
        finally { Environment.SetEnvironmentVariable(CopilotUsageReader.HomeVariable, null); }
    }

    // --- Read

    [Fact]
    public void Read_BaseAbsente_RetourneVideSansException()
    {
        Assert.Empty(CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_CompteursMappes_SansCompterLePromptTroisFois()
    {
        // input_tokens est le prompt entier : lectures et écritures de cache en sont un
        // sous-ensemble. Sans les soustraire, le même prompt serait compté trois fois.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteDatabase((1, "gpt-4.1", 1000, 60, 300, 100, utc));

        var e = Assert.Single(CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1)));

        Assert.Equal(600, e.Input);
        Assert.Equal(60, e.Output);
        Assert.Equal(300, e.CacheRead);
        Assert.Equal(100, e.CacheWrite);
        Assert.Equal(1060, e.Total);
        Assert.Equal("gpt-4.1", e.Model);
    }

    [Fact]
    public void Read_LigneHorsFenetre_Exclue()
    {
        var utc = DateTime.UtcNow;
        WriteDatabase(
            (1, "m", 10, 0, 0, 0, utc.AddDays(-40)),
            (2, "m", 20, 0, 0, 0, utc.AddMinutes(-5)));

        var e = Assert.Single(CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(20, e.Input);
    }

    [Fact]
    public void Read_ChaqueAppelCompte()
    {
        // Une ligne = un appel facturé, y compris ceux d'un sous-agent : ce sont des requêtes
        // distinctes, pas une copie du tour parent.
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteDatabase(
            (1, "m", 100, 10, 0, 0, utc),
            (2, "m", 200, 20, 0, 0, utc.AddSeconds(30)));

        var entries = CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1));

        Assert.Equal(2, entries.Count);
        Assert.Equal(330, entries.Sum(e => e.Total));
    }

    [Fact]
    public void Read_ClesDistinctesParLigne()
    {
        var utc = DateTime.UtcNow.AddMinutes(-10);
        WriteDatabase((1, "m", 10, 0, 0, 0, utc), (2, "m", 10, 0, 0, 0, utc));

        var entries = CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1));
        Assert.Equal(2, entries.Select(e => e.Key).Distinct().Count());
    }

    [Fact]
    public void Read_ColonnesNulles_TraiteesCommeZero()
    {
        var path = CopilotUsageReader.DatabasePath(_home);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
            CREATE TABLE assistant_usage_events (
                id INTEGER PRIMARY KEY, model TEXT, input_tokens INTEGER, output_tokens INTEGER,
                cache_read_tokens INTEGER, cache_write_tokens INTEGER, created_at TEXT);
            INSERT INTO assistant_usage_events (id, created_at) VALUES (1, $at);
            """;
            command.Parameters.AddWithValue("$at",
                DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var e = Assert.Single(CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
        Assert.Equal(0, e.Total);
        Assert.Equal("", e.Model);
    }

    [Fact]
    public void Read_TableAbsente_RetourneVideSansLever()
    {
        // Schéma changé par une mise à jour de Copilot : on n'affiche rien plutôt qu'un total faux.
        var path = CopilotUsageReader.DatabasePath(_home);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE autre_chose (id INTEGER)";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        Assert.Empty(CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    [Fact]
    public void Read_FichierQuiNEstPasUneBase_RetourneVideSansLever()
    {
        var path = CopilotUsageReader.DatabasePath(_home);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "ceci n'est pas une base SQLite");

        Assert.Empty(CopilotUsageReader.Read(_home, DateTime.Now.AddDays(-1)));
    }

    // --- Provider

    [Fact]
    public void Probe_BaseAbsenteEtDossierAbsent_NonInstalle()
    {
        Directory.CreateDirectory(_home);
        var probe = new CopilotUsageProvider(_home).Probe();

        Assert.False(probe.Available);
        Assert.Equal("non installé", probe.Detail);
    }

    [Fact]
    public void Probe_DossierPresentSansBase_SignaleLAbsenceDeDonnees()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".copilot"));
        var probe = new CopilotUsageProvider(_home).Probe();

        Assert.False(probe.Available);
        Assert.Contains("aucune donnée", probe.Detail);
    }

    [Fact]
    public async Task ReadAsync_NiQuotaNiCout()
    {
        // Copilot facture des requêtes premium sur un abonnement, pas des jetons : il n'y a pas de
        // montant à calculer, et aucun pourcentage de limite dans la base.
        WriteDatabase((1, "gpt-4.1", 100, 20, 0, 0, DateTime.UtcNow.AddMinutes(-5)));
        SqliteConnection.ClearAllPools();

        var usage = await new CopilotUsageProvider(_home).ReadAsync(CancellationToken.None);

        Assert.NotNull(usage);
        Assert.Equal(120, usage!.DayTokens);
        Assert.Null(usage.Session);
        Assert.Null(usage.Week);
        Assert.Equal("", usage.Cost);
    }
}
