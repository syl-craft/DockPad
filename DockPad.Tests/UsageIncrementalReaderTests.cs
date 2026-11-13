using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public abstract class UsageIncrementalReaderTests : IDisposable
{
    protected readonly string Home = Path.Combine(Path.GetTempPath(), "dockpad-incremental-" + Guid.NewGuid().ToString("N"));
    protected readonly DateTime Now = DateTime.Now.AddMinutes(-1);
    protected abstract string Root { get; }
    protected abstract string Line(string id, DateTime at, int input = 10);
    protected abstract List<UsageAggregator.UsageEntry> Read(DateTime since);
    protected abstract List<UsageAggregator.UsageEntry> ReadFull(DateTime since);
    protected abstract int FilesRead { get; }

    protected string Write(string name, params string[] lines)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "rollout-" + name + ".jsonl");
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }

    protected void AssertMatchesFull(DateTime since)
    {
        var expected = ReadFull(since);
        var actual = Read(since);
        Assert.Equal(expected, actual);
        Assert.Equal(ClaudeUsageReader.Aggregate(expected, Now), ClaudeUsageReader.Aggregate(actual, Now));
    }

    [Fact]
    public void LectureComplete_EntreesEtTotauxIdentiques()
    {
        Write("a", "", "{invalide", Line("ancien", Now.AddDays(-3)), Line("a", Now));
        Write("b", Line("b", Now, input: 27));
        AssertMatchesFull(Now.AddDays(-1));
        AssertMatchesFull(Now.AddDays(-5));
        AssertMatchesFull(Now.AddSeconds(1));
        Assert.Equal(2, FilesRead);
    }

    [Fact]
    public void Ajout_NeRelitPasLesEntreesDejaConsommees()
    {
        var path = Write("a", Line("a", Now));
        Assert.Single(Read(Now.AddDays(-1)));
        File.AppendAllText(path, Line("b", Now, input: 20) + "\r\n");
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Equal(2, Read(Now.AddDays(-1)).Count);
        Assert.Equal(2, FilesRead);
    }

    [Fact]
    public void Inchange_NEstPasRouvert_MemeSiLaDateChangeSeuleIlEstRelu()
    {
        var path = Write("a", Line("a", Now));
        Read(Now.AddDays(-1));
        // Le verrou exclusif prouve aussi l'absence d'ouverture, indépendamment du compteur.
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Single(Read(Now.AddDays(-1)));
        Assert.Equal(1, FilesRead);
        File.SetLastWriteTime(path, File.GetLastWriteTime(path).AddSeconds(1));
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Equal(2, FilesRead);
    }

    [Theory]
    [InlineData("é")]
    [InlineData("😀")]
    public void LignePartielle_CaractereCoupeEnOctets_ConsommeeUneSeuleFois(string character)
    {
        var path = Write("a", Line("a", Now));
        var line = Line("b", Now).Replace("\"timestamp\"", "\"note\":\"" + character + "\",\"timestamp\"");
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        var split = Encoding.UTF8.GetByteCount(line[..line.IndexOf(character, StringComparison.Ordinal)]) + 1;
        using (var stream = File.OpenWrite(path))
        {
            stream.Seek(0, SeekOrigin.End);
            stream.Write(bytes.AsSpan(0, split));
        }
        Assert.Single(Read(Now.AddDays(-1)));
        using (var stream = File.OpenWrite(path))
        {
            stream.Seek(0, SeekOrigin.End);
            // Même un JSON valide reste en attente tant que son saut de ligne n'est pas écrit.
            stream.Write(bytes.AsSpan(split, bytes.Length - split - 1));
        }
        Assert.Single(Read(Now.AddDays(-1)));
        File.AppendAllText(path, "\n");
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Equal(2, Read(Now.AddDays(-1)).Count);
    }

    [Fact]
    public void FichierRaccourci_EffaceLesAnciennesEntrees()
    {
        var path = Write("a", Line("a", Now), Line("b", Now));
        Assert.Equal(2, Read(Now.AddDays(-1)).Count);
        File.WriteAllText(path, Line("c", Now, input: 3) + "\n");
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Equal(3, Assert.Single(Read(Now.AddDays(-1))).Input);
    }

    [Fact]
    public void Utf8EtBom_LignesTraversantPlusieursBlocs()
    {
        var path = Write("a");
        var line = Line("a", Now).Replace("\"timestamp\"", "\"note\":\"" + new string('é', 70_000) + "😀\",\"timestamp\"");
        File.WriteAllText(path, line + "\r\n\r\n" + Line("b", Now) + "\n", new UTF8Encoding(true));
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Equal(2, Read(Now.AddDays(-1)).Count);
    }

    [Fact]
    public void FichierSupprimeOuHorsFenetre_EffaceSonEtat()
    {
        var path = Write("a", Line("a", Now));
        Read(Now.AddDays(-1));
        File.SetLastWriteTime(path, Now.AddDays(-4));
        Assert.Empty(Read(Now.AddDays(-1)));
        File.SetLastWriteTime(path, DateTime.Now);
        Assert.Single(Read(Now.AddDays(-1)));
        Assert.Equal(2, FilesRead);
        File.Delete(path);
        Assert.Empty(Read(Now.AddDays(-1)));
    }

    [Fact]
    public async Task LecturesChevauchees_NeDupliquentPasLesEntrees()
    {
        Write("a", Enumerable.Range(0, 100).Select(i => Line(i.ToString(), Now)).ToArray());
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Read(Now.AddDays(-1)))));
        Assert.All(snapshots, entries => Assert.Equal(100, entries.Count));
        Assert.Equal(1, FilesRead);
    }

    public virtual void Dispose()
    {
        if (Directory.Exists(Home)) Directory.Delete(Home, recursive: true);
    }
}

public sealed class ClaudeUsageIncrementalTests : UsageIncrementalReaderTests
{
    private readonly ClaudeTranscriptIndex _index = new();
    protected override string Root => Path.Combine(Home, ".claude", "projects");
    protected override int FilesRead => _index.FilesRead;
    protected override List<UsageAggregator.UsageEntry> Read(DateTime since) => _index.Read(Home, since);
    protected override List<UsageAggregator.UsageEntry> ReadFull(DateTime since) => ClaudeUsageReader.Read(Home, since);
    protected override string Line(string id, DateTime at, int input = 10) => JsonSerializer.Serialize(new
    {
        type = "assistant", requestId = "r", timestamp = at.ToUniversalTime().ToString("O"),
        message = new { id, model = "claude-sonnet-5", usage = new { input_tokens = input, output_tokens = 2 } },
    });

    [Fact]
    public void DoublonsEntreFichiers_PremiereOccurrenceGagne_ApresFiltreDeDate()
    {
        Write("a", Line("m", Now.AddDays(-3), input: 1), Line("m", Now, input: 20));
        Write("b", Line("m", Now, input: 99));
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Equal(20, Assert.Single(Read(Now.AddDays(-1))).Input);
        Assert.Equal(1, Assert.Single(Read(Now.AddDays(-5))).Input);
        // Un ajout au premier fichier doit gagner même s'il est lu après le second au tick suivant.
        File.AppendAllText(Path.Combine(Root, "rollout-a.jsonl"), Line("nouveau", Now, input: 30) + "\n");
        Write("c", Line("nouveau", Now, input: 90));
        AssertMatchesFull(Now.AddDays(-1));
    }

    [Fact]
    public void Doublon_SuppressionDuPremierFichier_LaSecondeOccurrenceGagne()
    {
        var first = Write("a", Line("m", Now, input: 20));
        Write("b", Line("m", Now, input: 99));
        Assert.Equal(20, Assert.Single(Read(Now.AddDays(-1))).Input);
        File.Delete(first);
        Assert.Equal(99, Assert.Single(Read(Now.AddDays(-1))).Input);
    }
}

public sealed class CodexUsageIncrementalTests : UsageIncrementalReaderTests
{
    private readonly CodexTranscriptIndex _index = new();
    private readonly string? _savedHome = Environment.GetEnvironmentVariable(CodexUsageReader.HomeVariable);
    public CodexUsageIncrementalTests() => Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, null);
    protected override string Root => Path.Combine(Home, ".codex", "sessions");
    protected override int FilesRead => _index.FilesRead;
    protected override List<UsageAggregator.UsageEntry> Read(DateTime since) => _index.ReadSnapshot(Home, since, since).Entries;
    protected override List<UsageAggregator.UsageEntry> ReadFull(DateTime since) => CodexUsageReader.Read(Home, since);
    protected override string Line(string id, DateTime at, int input = 10) => JsonSerializer.Serialize(new
    {
        type = "event_msg", timestamp = at.ToUniversalTime().ToString("O"),
        payload = new { type = "token_count", info = new { last_token_usage = new { input_tokens = input, output_tokens = 2 } } },
    });

    [Fact]
    public void ModeleEtIndex_ContinuentEntreLesMorceaux()
    {
        var path = Write("a", "", """{"type":"turn_context","payload":{"model":"gpt-6-sol"}}""");
        Assert.Empty(Read(Now.AddDays(-1)));
        File.AppendAllText(path, Line("a", Now) + "\n");
        var entry = Assert.Single(Read(Now.AddDays(-1)));
        Assert.Equal("gpt-6-sol", entry.Model);
        Assert.Equal("codex|rollout-a|3", entry.Key);
        File.AppendAllText(path, "\n" + Line("b", Now) + "\n");
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Equal("codex|rollout-a|5", Read(Now.AddDays(-1))[1].Key);
    }

    private string Quota(DateTime at, int pct) => JsonSerializer.Serialize(new
    {
        type = "event_msg", timestamp = at.ToUniversalTime().ToString("O"),
        payload = new { type = "token_count", rate_limits = new { primary = new { used_percent = pct, window_minutes = 300 } } },
    });

    [Fact]
    public void Quota_LePlusRecentParHorodatage_AvecUneBorneDistincte()
    {
        var path = Write("a", Quota(Now.AddDays(-2), 40), Quota(Now.AddDays(-3), 80));
        File.SetLastWriteTime(path, Now.AddDays(-2));
        Write("b", Line("b", Now), Quota(Now.AddDays(-4), 10));
        var snapshot = _index.ReadSnapshot(Home, Now.AddDays(-1), Now.AddDays(-7));
        Assert.Single(snapshot.Entries);
        Assert.Equal(40, snapshot.Quota?.Session?.UsedPct);
        File.AppendAllText(path, Quota(Now, 50) + "\n");
        var expected = CodexUsageReader.ReadSnapshot(Home, Now.AddDays(-1), Now.AddDays(-7));
        snapshot = _index.ReadSnapshot(Home, Now.AddDays(-1), Now.AddDays(-7));
        Assert.Equal(expected.Entries, snapshot.Entries);
        Assert.Equal(expected.Quota?.ObservedAt, snapshot.Quota?.ObservedAt);
        Assert.Equal(50, snapshot.Quota?.Session?.UsedPct);
        File.Delete(path);
        Assert.Equal(10, _index.ReadSnapshot(Home, Now.AddDays(-1), Now.AddDays(-7)).Quota?.Session?.UsedPct);
    }

    [Fact]
    public void Annulation_RespecteeMemePourUnFichierInchangeOuUnDossierAbsent()
    {
        Write("a", Line("a", Now));
        Read(Now.AddDays(-1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => _index.ReadSnapshot(Home, Now, Now, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => _index.ReadSnapshot(Home + "absent", Now, Now, cancellation.Token));
    }

    [Fact]
    public void AnnulationPendantUnBloc_ArreteAvantLaLigneSuivante()
    {
        var path = Write("a", Line("a", Now), Line("b", Now));
        using var cancellation = new CancellationTokenSource();
        var consumed = 0;
        Assert.Throws<OperationCanceledException>(() => JsonlIncrementalReader.Read(path, position: 0,
            length: new FileInfo(path).Length, consume: _ =>
            {
                consumed++;
                cancellation.Cancel();
            }, cancellation.Token));
        Assert.Equal(1, consumed);
        AssertMatchesFull(Now.AddDays(-1));
    }

    [Fact]
    public void Archivage_DonneLesMemesEntrees_SansLesConserverDeuxFois()
    {
        var path = Write("a", Line("a", Now));
        Read(Now.AddDays(-1));
        var archives = Path.Combine(Home, ".codex", "archived_sessions");
        Directory.CreateDirectory(archives);
        File.Move(path, Path.Combine(archives, Path.GetFileName(path)));
        AssertMatchesFull(Now.AddDays(-1));
        Assert.Single(Read(Now.AddDays(-1)));
    }

    public override void Dispose()
    {
        Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, _savedHome);
        base.Dispose();
    }
}
