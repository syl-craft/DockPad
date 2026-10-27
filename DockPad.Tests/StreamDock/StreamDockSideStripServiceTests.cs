using System.Collections.Concurrent;
using DockPad.Models;
using DockPad.Services.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Colonne à la connexion, mesures périodiques, déduplication, changement de rendu et lectures tardives.
/// Appuis malgré les échecs dédupliqués, annulations, reconnexion et fin des rendus sans publication tardive.
/// </summary>
public class StreamDockSideStripServiceTests
{
    [Fact]
    public async Task Colonne_DessineeALaConnexion()
    {
        await using var dock = new TestDock();
        await dock.Start();
        Assert.Equal(1, dock.Load.Resets);
        Assert.Equal(1, dock.Load.Samples);
        Assert.Equal(SideStripStyle.Columns, dock.Service.SideStripStyle);
        var image = Assert.Single(dock.Surface.SideStrips);
        Assert.Equal(75, image.Width);
        Assert.Equal(380, image.Height);
        dock.AssertColumn();
    }

    [Fact]
    public async Task Colonne_MesureConnexion_SerialiseeApresLesTouches()
    {
        await using var dock = new TestDock();
        var samplesBeforeKeys = new ConcurrentQueue<int>();
        dock.Surface.BeforeImage = () => samplesBeforeKeys.Enqueue(dock.Load.Samples);
        await dock.Start();
        Assert.Equal(15, samplesBeforeKeys.Count);
        Assert.All(samplesBeforeKeys, samples => Assert.Equal(0, samples));
        Assert.Equal(1, dock.Load.Samples);
    }

    [Fact]
    public async Task Colonne_MesureToutesLes5s()
    {
        await using var dock = new TestDock();
        await dock.Start();
        await dock.Advance(4999);
        Assert.Equal(1, dock.Load.Samples);
        dock.Load.Value = new SystemLoad(42, 70);
        await dock.Advance(1);
        Assert.Equal(2, dock.Load.Samples);
        Assert.Equal(2, dock.Surface.SideStrips.Count);
        dock.AssertColumn();
        await dock.Advance(5000);
        Assert.Equal(3, dock.Load.Samples);
    }

    [Fact]
    public async Task Colonne_MesureIdentique_RienRenvoye()
    {
        await using var dock = new TestDock();
        await dock.Start();
        int commits = dock.Surface.Commits;
        int renders = dock.Renderer.SideStrips.Count;
        await dock.Advance(10000);
        Assert.Equal(3, dock.Load.Samples);
        Assert.True(dock.Renderer.SideStrips.Count > renders);
        Assert.Single(dock.Surface.SideStrips);
        Assert.Equal(commits, dock.Surface.Commits);
    }

    [Fact]
    public async Task Colonne_ChangementDeRendu_RedessineeSansAttendre()
    {
        await using var dock = new TestDock();
        await dock.Start();
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await dock.Idle();
        Assert.Equal(1, dock.Load.Samples);
        Assert.Equal(2, dock.Surface.SideStrips.Count);
        dock.AssertColumn();
        int renders = dock.Renderer.SideStrips.Count;
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await dock.Idle();
        Assert.Equal(renders, dock.Renderer.SideStrips.Count);
    }

    [Fact]
    public async Task Colonne_IaPrincipaleChangee_RedessineeAvecSesFenetresEnTete()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(80), Usage(20, "codex")];
        await dock.Start();
        Assert.Equal("claude", dock.Renderer.SideStrips.Last().Content.Quotas[0].ProviderId);

        dock.Service.PrincipalProviderId = "codex";
        await dock.Idle();
        Assert.Equal(1, dock.Load.Samples);
        Assert.Equal("codex", dock.Renderer.SideStrips.Last().Content.Quotas[0].ProviderId);
        dock.AssertColumn();

        int renders = dock.Renderer.SideStrips.Count;
        dock.Service.PrincipalProviderId = "Codex";
        await dock.Idle();
        Assert.Equal(renders, dock.Renderer.SideStrips.Count);
    }

    [Fact]
    public async Task Colonne_LectureTardive_GardeRenduEtMesureRecents()
    {
        await using var dock = new TestDock();
        var reading = new TaskCompletionSource<IReadOnlyList<AiUsage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Sources.ReadUsages = _ => reading.Task;
        await dock.Start(includeUsages: false);
        Assert.Empty(dock.Renderer.SideStrips.Last().Content.Quotas);
        dock.Service.SideStripStyle = SideStripStyle.HeroStack;
        dock.Load.Value = new SystemLoad(82, 73);
        await dock.Advance(5000, includeUsages: false);
        reading.SetResult([Usage(67)]);
        await dock.Idle();
        var latest = dock.Renderer.SideStrips.Last();
        Assert.Equal(SideStripStyle.HeroStack, latest.Style);
        Assert.Equal(82, latest.Content.CpuPct);
        Assert.Equal(73, latest.Content.MemoryPct);
        Assert.Equal(67, Assert.Single(latest.Content.Quotas).UsedPct);
        dock.Sources.Usages = [Usage(67)];
        dock.AssertColumn();
    }

    [Fact]
    public async Task Colonne_ConfigModifieeLectureEnEchec_GardeLeRenduEnCours()
    {
        await using var dock = new TestDock();
        await dock.Start();
        var rendering = NewGate();
        dock.Renderer.SideStripGate = rendering;
        dock.Renderer.SideStripStarted = NewGate();
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await Bounded(dock.Renderer.SideStripStarted.Task);
        var reading = NewGate();
        dock.Sources.ReadUsages = _ =>
        {
            reading.TrySetResult();
            return Task.FromException<IReadOnlyList<AiUsage>>(new InvalidOperationException("Fake usage read failed."));
        };
        dock.Service.OnUsageConfigChanged();
        await Bounded(reading.Task);
        rendering.SetResult();
        await dock.Idle();
        Assert.Equal(2, dock.Surface.SideStrips.Count);
        dock.AssertColumn();
    }

    [Fact]
    public async Task Colonne_Deconnexion_PlusAucunePublication()
    {
        await using var dock = new TestDock();
        await dock.Start();
        var rendering = NewGate();
        dock.Renderer.SideStripGate = rendering;
        dock.Renderer.SideStripStarted = NewGate();
        dock.Load.Value = new SystemLoad(99, 80);
        dock.Time.Advance(TimeSpan.FromSeconds(5));
        await Bounded(dock.Renderer.SideStripStarted.Task);
        int columns = dock.Surface.SideStrips.Count;
        int samples = dock.Load.Samples;
        dock.Driver.Surface = null;
        dock.Surface.Disconnect();
        await dock.Idle();
        var completed = dock.Service.WhenSideStripRendersCompleteAsync();
        Assert.False(completed.IsCompleted);
        rendering.SetResult();
        await Bounded(completed);
        await dock.Advance(60000);
        Assert.Equal(columns, dock.Surface.SideStrips.Count);
        Assert.Equal(samples, dock.Load.Samples);
    }

    [Fact]
    public async Task Colonne_BandeauDesactive_ProcesseurEtMemoireSansLecture()
    {
        await using var dock = new TestDock();
        dock.Sources.Config.Enabled = false;
        dock.Sources.Usages = [Usage(90)];
        await dock.Start();
        await dock.Advance(60000);
        Assert.Empty(dock.Sources.UsageReads);
        Assert.Empty(dock.Renderer.SideStrips.Last().Content.Quotas);
        Assert.Equal(23, dock.Renderer.SideStrips.Last().Content.CpuPct);
        Assert.Equal(61, dock.Renderer.SideStrips.Last().Content.MemoryPct);
        dock.AssertColumn();
    }

    [Fact]
    public async Task Colonne_SurfaceSansColonne_RienNiMesure()
    {
        await using var dock = new TestDock();
        dock.Surface.Description = dock.Surface.Description with { SideStrip = null };
        await dock.Start();
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        dock.Service.OnUsageConfigChanged();
        await dock.Advance(120000);
        Assert.Equal(0, dock.Load.Resets);
        Assert.Equal(0, dock.Load.Samples);
        Assert.Empty(dock.Sources.UsageReads);
        Assert.Empty(dock.Renderer.SideStrips);
        Assert.Empty(dock.Surface.SideStrips);
        Assert.Equal(15, dock.Surface.Images.Count);
    }

    [Fact]
    public async Task Colonne_AppuiPendantRendu_AppuiDAbord()
    {
        await using var dock = new TestDock();
        await dock.Start();
        dock.Surface.Press(0);
        dock.Surface.Release(0);
        await dock.Idle();
        var rendering = NewGate();
        dock.Renderer.SideStripGate = rendering;
        dock.Renderer.SideStripStarted = NewGate();
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await Bounded(dock.Renderer.SideStripStarted.Task);
        int columns = dock.Surface.SideStrips.Count;
        var launched = NewGate();
        dock.Sources.AfterRun = () => launched.TrySetResult();
        dock.Surface.Press(1);
        dock.Surface.Release(1);
        await Bounded(launched.Task);
        Assert.Single(dock.Sources.Runs);
        Assert.Equal(columns, dock.Surface.SideStrips.Count);
        rendering.SetResult();
        await dock.Idle();
        Assert.Equal(columns + 1, dock.Surface.SideStrips.Count);
    }

    [Fact]
    public async Task Colonne_RenduDepasse_NonPublie()
    {
        await using var dock = new TestDock();
        await dock.Start();
        var rendering = NewGate();
        dock.Renderer.SideStripGate = rendering;
        dock.Renderer.SideStripStarted = NewGate();
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await Bounded(dock.Renderer.SideStripStarted.Task);
        dock.Renderer.SideStripGate = null;
        var published = NewGate();
        dock.Surface.AfterCommit = () => published.TrySetResult();
        dock.Service.SideStripStyle = SideStripStyle.Reservoir;
        await Bounded(published.Task);
        Assert.Equal(2, dock.Surface.SideStrips.Count);
        rendering.SetResult();
        await dock.Idle();
        Assert.Equal(2, dock.Surface.SideStrips.Count);
        dock.AssertColumn();
    }

    [Fact]
    public async Task Colonne_Reconnexion_ReinitialiseEtRenvoie()
    {
        await using var dock = new TestDock();
        await dock.Start();
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        dock.Surface.Disconnect();
        await dock.Idle();
        Assert.Equal(2, dock.Load.Resets);
        Assert.Equal(2, dock.Load.Samples);
        Assert.Equal(dock.Surface.SideStrips.Last().Bgra32, Assert.Single(replacement.SideStrips).Bgra32);
    }

    [Fact]
    public async Task Colonne_ArretPendantRendu_AucunEnvoiTardif()
    {
        await using var dock = new TestDock();
        await dock.Start();
        var rendering = NewGate();
        dock.Renderer.SideStripGate = rendering;
        dock.Renderer.SideStripStarted = NewGate();
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await Bounded(dock.Renderer.SideStripStarted.Task);
        await Bounded(dock.Service.DisposeAsync().AsTask());
        var completed = dock.Service.WhenSideStripRendersCompleteAsync();
        Assert.False(completed.IsCompleted);
        rendering.SetResult();
        await Bounded(completed);
        dock.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Single(dock.Surface.SideStrips);
        Assert.Equal(1, dock.Load.Samples);
    }

    [Fact]
    public async Task Colonne_EnvoiAnnule_RetenteSansDeconnexionEtJournalDedupplique()
    {
        await using var dock = new TestDock();
        dock.Surface.BeforeSideStrip = _ => Task.FromException(new OperationCanceledException("Cleared during encoding."));
        await dock.Start();
        await dock.Advance(10000);
        Assert.Empty(dock.Surface.SideStrips);
        Assert.Equal(1, dock.Driver.Opens);
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Single(failures);
        dock.Surface.BeforeSideStrip = null;
        await dock.Advance(5000);
        Assert.Single(dock.Surface.SideStrips);
    }

    [Fact]
    public async Task Colonne_DeconnexionPendantEnvoi_AucunCommitTardif()
    {
        await using var dock = new TestDock();
        await dock.Start();
        var sending = NewGate();
        var started = NewGate();
        dock.Surface.BeforeSideStrip = async _ => { started.TrySetResult(); await sending.Task; };
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await Bounded(started.Task);
        int commits = dock.Surface.Commits;
        dock.Driver.Surface = null;
        dock.Surface.Disconnect();
        await dock.Idle();
        sending.SetResult();
        await dock.Idle();
        Assert.Single(dock.Surface.SideStrips);
        Assert.Equal(commits, dock.Surface.Commits);
    }

    [Fact]
    public async Task Colonne_CommitAnnule_ImageIdentiqueRetentee()
    {
        await using var dock = new TestDock();
        await dock.Start();
        int commits = dock.Surface.Commits;
        dock.Surface.CommitFailure = new OperationCanceledException("Commit canceled.");
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await dock.Idle();
        Assert.Equal(2, dock.Surface.SideStrips.Count);
        Assert.Equal(commits, dock.Surface.Commits);
        dock.Surface.CommitFailure = null;
        await dock.Advance(5000);
        Assert.Equal(3, dock.Surface.SideStrips.Count);
        Assert.Equal(commits + 1, dock.Surface.Commits);
        Assert.Equal(dock.Surface.SideStrips.ElementAt(1).Bgra32, dock.Surface.SideStrips.Last().Bgra32);
        Assert.Equal(1, dock.Driver.Opens);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Colonne_MesureEnEchec_GardeLaSessionEtJournalDedupplique(bool aLaConnexion)
    {
        await using var dock = new TestDock();
        var failure = new InvalidOperationException("Fake load sample failed.");
        if (aLaConnexion) dock.Load.Failure = failure;
        await dock.Start();
        var previous = dock.Surface.SideStrips.Last();
        dock.Load.Failure = failure;
        await dock.Advance(5000);
        await dock.Advance(5000);
        Assert.Equal(previous.Bgra32, dock.Surface.SideStrips.Last().Bgra32);
        await dock.AssertKeysStillWork();
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        Assert.StartsWith("Stream Dock : ", failures[0].Message);
        dock.Load.Failure = null;
        dock.Load.Value = new SystemLoad(42, 70);
        await dock.Advance(5000);
        dock.AssertColumn();
        dock.Load.Failure = failure;
        await dock.Advance(5000);
        Assert.Equal(2, dock.Logs.Where(line => line.Cause != null).Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Colonne_RenduEnEchec_GardeLaSessionEtJournalDedupplique(bool aLaConnexion)
    {
        await using var dock = new TestDock();
        var failure = new InvalidOperationException("Fake side strip render failed.");
        if (aLaConnexion) dock.Renderer.SideStripFailure = failure;
        await dock.Start();
        int columns = dock.Surface.SideStrips.Count;
        dock.Renderer.SideStripFailure = failure;
        await dock.Advance(5000);
        await dock.Advance(5000);
        Assert.Equal(columns, dock.Surface.SideStrips.Count);
        await dock.AssertKeysStillWork();
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        Assert.StartsWith("Stream Dock : ", failures[0].Message);
        dock.Renderer.SideStripFailure = null;
        dock.Load.Value = new SystemLoad(42, 70);
        await dock.Advance(5000);
        dock.AssertColumn();
        dock.Renderer.SideStripFailure = failure;
        await dock.Advance(5000);
        Assert.Equal(2, dock.Logs.Where(line => line.Cause != null).Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Colonne_EnvoiEnEchec_GardeLaSessionEtJournalDedupplique(bool auCommit)
    {
        await using var dock = new TestDock();
        await dock.Start();
        var failure = new ArgumentException("Fake side strip write failed.");
        if (auCommit) dock.Surface.CommitFailure = failure;
        else dock.Surface.BeforeSideStrip = _ => Task.FromException(failure);
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await dock.Idle();
        await dock.Advance(5000);
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        Assert.StartsWith("Stream Dock : ", failures[0].Message);
        dock.Surface.CommitFailure = null;
        await dock.AssertKeysStillWork();
        dock.Surface.BeforeSideStrip = null;
        int columns = dock.Surface.SideStrips.Count;
        await dock.Advance(5000);
        Assert.Equal(columns + 1, dock.Surface.SideStrips.Count);
        dock.AssertColumn();
    }

    [Fact]
    public async Task Colonne_EnvoiIOException_FermeLaSessionEtReprend()
    {
        await using var dock = new TestDock();
        await dock.Start();
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        var failure = new System.IO.IOException("Fake surface disconnected during write.");
        dock.Surface.BeforeSideStrip = _ => Task.FromException(failure);
        dock.Service.SideStripStyle = SideStripStyle.Rings;
        await dock.Idle();
        await Bounded(dock.Surface.DisposeStarted.Task);
        Assert.Single(dock.Surface.SideStrips);
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        await dock.Advance(5000);
        Assert.Equal(2, dock.Driver.Opens);
        Assert.Single(replacement.SideStrips);
    }

    private static AiUsage Usage(int used, string providerId = "claude") => new()
    {
        ProviderId = providerId, Name = providerId, Glyph = "C", AccentColor = "#FFFFFF",
        Session = new UsageWindow { UsedPct = used },
    };

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Bounded(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5));

    private sealed class TestDock : IAsyncDisposable
    {
        public ManualTimeProvider Time { get; } = new();
        public FakeSurfaceDriver Driver { get; } = new();
        public FakeSurface Surface { get; }
        public FakeDockSources Sources { get; } = new() { Tiles = [new ShortcutEntry { Name = "Tile", Command = "sample.exe" }] };
        public FakeDockRenderer Renderer { get; } = new();
        public FakeSystemLoadMonitor Load { get; } = new();
        public StreamDockService Service { get; }
        public ConcurrentQueue<(string Message, Exception? Cause)> Logs { get; } = new();

        public TestDock()
        {
            Surface = Driver.Surface ?? throw new InvalidOperationException("No fake surface.");
            Service = new StreamDockService(Driver, Sources, Renderer, Time, Load,
                (message, cause) => Logs.Enqueue((message, cause)));
        }

        public async Task Start(bool includeUsages = true)
        {
            Service.Start();
            await Idle(includeUsages);
        }

        public Task Idle(bool includeUsages = true) => Bounded(Service.WhenIdleAsync(includeUsages));

        public async Task Advance(int milliseconds, bool includeUsages = true)
        {
            Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            await Idle(includeUsages);
        }

        public void AssertColumn()
        {
            var content = SideStripPlanner.Plan(Sources.Usages, Sources.Config, Load.Value,
                principalProviderId: Service.PrincipalProviderId);
            var size = Surface.Description.SideStrip ?? throw new InvalidOperationException("No side strip.");
            var expected = FakeDockRenderer.CreateSideStripImage(content, Service.SideStripStyle, size, Renderer.SideStripVersion);
            Assert.Equal(expected.Bgra32, Surface.SideStrips.Last().Bgra32);
        }

        public async Task AssertKeysStillWork()
        {
            Surface.Press(0);
            Surface.Release(0);
            await Idle();
            Surface.Press(1);
            Surface.Release(1);
            await Idle();
            Assert.Single(Sources.Runs);
            Assert.Equal(1, Driver.Opens);
            Assert.Equal(0, Surface.DisposeCalls);
        }

        public ValueTask DisposeAsync() => new(Bounded(Service.DisposeAsync().AsTask()));
    }
}
