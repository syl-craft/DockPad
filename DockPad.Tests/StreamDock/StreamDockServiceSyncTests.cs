using System.Collections.Concurrent;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.StreamDock;

namespace DockPad.Tests;

/// <summary>
/// Synchro bidirectionnelle sans boucle, reconnexion et maintien après modification de la grille.
/// Activation à chaud, publication validée, quotas périodiques et annulation au changement de configuration ou à l'arrêt.
/// </summary>
public class StreamDockServiceSyncTests
{
    [Fact]
    public async Task Synchro_PageDockPadOuvreLaPageSurLeDock()
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        var group = Tile(0, page: 0);
        group.Layout = TileLayout.Quad;
        group.Children = [Tile(0)];
        dock.Sources.Tiles = [group, Tile(0, page: 2)];
        await dock.Start();
        dock.Service.OnShortcutPageChanged(0, PageChangeOrigin.DockPad);
        await dock.Idle();
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        await dock.Click(1);
        Assert.IsType<DockScreen.Group>(dock.Service.Screen);
        dock.Service.OnShortcutPageChanged(2, PageChangeOrigin.DockPad);
        await dock.Idle();
        Assert.Equal(new DockScreen.Page(2, 0), dock.Service.Screen);
        Assert.Empty(dock.Sources.ShownPages);
    }

    [Fact]
    public async Task Synchro_PageVideDockPad_DockNeBougePas()
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        await dock.Start();
        await dock.Click(0);
        int commits = dock.Surface.Commits;
        dock.Service.OnShortcutPageChanged(1, PageChangeOrigin.DockPad);
        await dock.Idle();
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        Assert.Equal(commits, dock.Surface.Commits);
    }

    [Fact]
    public async Task Synchro_PageDock_MetAJourDockPad()
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        await dock.Start();
        await dock.Click(1);
        Assert.Equal(new DockScreen.Page(2, 0), dock.Service.Screen);
        Assert.Equal(2, Assert.Single(dock.Sources.ShownPages));
        Assert.Equal(2, dock.Sources.ShortcutPage);
    }

    [Fact]
    public async Task Synchro_PasDeBoucle()
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        dock.Sources.AfterShowPage = page => dock.Service.OnShortcutPageChanged(page, PageChangeOrigin.StreamDock);
        await dock.Start();
        int commits = dock.Surface.Commits;
        int renders = dock.Renderer.Keys.Count;
        await dock.Click(1);
        Assert.Single(dock.Sources.ShownPages);
        Assert.Equal(commits + 3, dock.Surface.Commits);
        Assert.Equal(renders + 30, dock.Renderer.Keys.Count);
        dock.Service.OnShortcutPageChanged(0, PageChangeOrigin.StreamDock);
        await dock.Idle();
        Assert.Equal(new DockScreen.Page(2, 0), dock.Service.Screen);
        Assert.Equal(commits + 3, dock.Surface.Commits);
        dock.Service.OnShortcutPageChanged(0, PageChangeOrigin.DockPad);
        await dock.Idle();
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        Assert.Single(dock.Sources.ShownPages);
        Assert.Equal(commits + 4, dock.Surface.Commits);
    }

    [Fact]
    public async Task Synchro_AccueilSuiteGroupe_SansEffet()
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        var entries = Enumerable.Range(0, 15).Select(index => Tile(index)).ToArray();
        entries[14].Layout = TileLayout.Quad;
        entries[14].Children = [Tile(0)];
        dock.Sources.Tiles = entries;
        await dock.Start();
        await dock.Click(0);
        dock.Sources.ShownPages.Clear();
        await dock.Click(14);
        Assert.Equal(new DockScreen.Page(0, 1), dock.Service.Screen);
        await dock.Click(2);
        Assert.IsType<DockScreen.Group>(dock.Service.Screen);
        await dock.Click(0);
        Assert.Equal(new DockScreen.Page(0, 1), dock.Service.Screen);
        await dock.Click(0);
        Assert.IsType<DockScreen.Home>(dock.Service.Screen);
        Assert.Empty(dock.Sources.ShownPages);
        dock.Sources.Tiles = Enumerable.Range(0, 16).Select(page => Tile(0, page)).ToArray();
        dock.Service.OnGridChanged();
        await dock.Advance(30);
        await dock.Click(14);
        Assert.Equal(new DockScreen.Home(1), dock.Service.Screen);
        Assert.Empty(dock.Sources.ShownPages);
    }

    [Fact]
    public async Task SansSynchro_AucunEffetCroise()
    {
        await using var dock = new TestDock();
        Assert.False(dock.Service.SyncPages);
        dock.Sources.ShortcutPage = 2;
        await dock.Start();
        Assert.IsType<DockScreen.Home>(dock.Service.Screen);
        dock.Service.OnShortcutPageChanged(2, PageChangeOrigin.DockPad);
        await dock.Idle();
        Assert.IsType<DockScreen.Home>(dock.Service.Screen);
        await dock.Click(0);
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        Assert.Empty(dock.Sources.ShownPages);
        Assert.Equal(2, dock.Sources.ShortcutPage);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Synchro_ALaConnexion_OuvreLaPageCourante(int page)
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        dock.Sources.ShortcutPage = page;
        await dock.Start();
        DockScreen expected = page == 2 ? new DockScreen.Page(2, 0) : new DockScreen.Home(0);
        Assert.Equal(expected, dock.Service.Screen);
        dock.Sources.ShortcutPage = 0;
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        dock.Surface.Disconnect();
        await dock.Idle();
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        Assert.Equal(1, replacement.Commits);
        Assert.Equal(2, dock.Sources.UsageReads.Count);
        Assert.Empty(dock.Sources.ShownPages);
    }

    [Fact]
    public async Task GrilleModifiee_PageSupprimee_RetourAccueil()
    {
        await using var dock = new TestDock();
        await dock.Start();
        await dock.Click(1);
        dock.Sources.Tiles = [Tile(0)];
        dock.Service.OnGridChanged();
        await dock.Advance(30);
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
    }

    [Fact]
    public async Task GrilleModifiee_AJourEnMoinsDUneSeconde()
    {
        await using var dock = new TestDock();
        await dock.Start();
        await dock.Click(0);
        int images = dock.Surface.Images.Count;
        dock.Sources.Tiles[0].Name = "Updated";
        dock.Service.OnGridChanged();
        await dock.Advance(29);
        Assert.Equal(images, dock.Surface.Images.Count);
        await dock.Advance(1);
        Assert.Equal(images + 1, dock.Surface.Images.Count);
        await dock.Click(1);
        Assert.Equal("Updated", Assert.Single(dock.Sources.Runs).Name);
    }

    [Fact]
    public async Task Quotas_RafraichisChaqueMinute()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        Assert.Single(dock.Sources.UsageReads);
        await dock.AssertDisplays();
        int keys = dock.Surface.Images.Count;
        int displays = dock.Surface.Displays.Count;
        int commits = dock.Surface.Commits;
        dock.Sources.Usages = [Usage(20)];
        await dock.Advance(59999);
        Assert.Single(dock.Sources.UsageReads);
        await dock.Advance(1);
        Assert.Equal(2, dock.Sources.UsageReads.Count);
        Assert.Equal(displays + 1, dock.Surface.Displays.Count);
        Assert.Equal(0, dock.Surface.Displays.Last().Display);
        Assert.Equal(keys, dock.Surface.Images.Count);
        Assert.Equal(commits + 1, dock.Surface.Commits);
        await dock.AssertDisplays();
        dock.Sources.Usages = [Usage(30)];
        await dock.Advance(60000);
        Assert.Equal(3, dock.Sources.UsageReads.Count);
        await dock.AssertDisplays();
    }

    [Fact]
    public async Task Quotas_LectureEnCours_TicSaute()
    {
        await using var dock = new TestDock();
        var read = NewRead();
        dock.Sources.ReadUsages = ct => read.Task.WaitAsync(ct);
        await dock.Start(includeUsages: false);
        Assert.Single(dock.Sources.UsageReads);
        await dock.Advance(120000, includeUsages: false);
        Assert.Single(dock.Sources.UsageReads);
        await dock.Click(0, includeUsages: false);
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        read.SetResult([Usage(10)]);
        await dock.Idle();
        Assert.Single(dock.Sources.UsageReads);
        await dock.Advance(60000);
        Assert.Equal(2, dock.Sources.UsageReads.Count);
    }

    [Fact]
    public async Task Quotas_Inchanges_RienNEstRenvoye()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        int keys = dock.Surface.Images.Count;
        int displays = dock.Surface.Displays.Count;
        int commits = dock.Surface.Commits;
        dock.Sources.Usages = [Usage(10)];
        await dock.Advance(60000);
        Assert.Equal(2, dock.Sources.UsageReads.Count);
        Assert.Equal(keys, dock.Surface.Images.Count);
        Assert.Equal(displays, dock.Surface.Displays.Count);
        Assert.Equal(commits, dock.Surface.Commits);
    }

    [Fact]
    public async Task Quotas_BandeauDesactive_EcransNoirsSansLecture()
    {
        await using var dock = new TestDock();
        dock.Sources.Config.Enabled = false;
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        int renders = dock.Renderer.Displays.Count;
        await dock.Advance(60000);
        await dock.Advance(60000);
        Assert.Equal(renders, dock.Renderer.Displays.Count);
        Assert.Empty(dock.Sources.UsageReads);
        Assert.Equal(3, dock.Surface.Displays.Count);
        Assert.Equal(1, dock.Surface.Commits);
        await dock.AssertDisplays();
    }

    [Fact]
    public async Task Quotas_LectureEnEchec_EcransInchanges()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        int displays = dock.Surface.Displays.Count;
        var failure = new InvalidOperationException("Fake usage read failed.");
        dock.Sources.ReadUsages = _ => Task.FromException<IReadOnlyList<AiUsage>>(failure);
        int commits = dock.Surface.Commits;
        await dock.Advance(60000);
        Assert.Equal(commits, dock.Surface.Commits);
        await dock.Click(0);
        await dock.Advance(60000);
        Assert.Equal(displays, dock.Surface.Displays.Count);
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        Assert.Equal(1, dock.Driver.Opens);
        Assert.Equal(0, dock.Surface.DisposeCalls);
        dock.Sources.ReadUsages = null;
        dock.Sources.Usages = [Usage(30)];
        await dock.Advance(60000);
        Assert.Equal(displays + 1, dock.Surface.Displays.Count);
        dock.Sources.ReadUsages = _ => Task.FromException<IReadOnlyList<AiUsage>>(failure);
        await dock.Advance(60000);
        Assert.Equal(2, dock.Logs.Where(line => line.Cause != null).Count());
    }

    [Fact]
    public async Task Arret_AnnuleLaLectureDesQuotas()
    {
        await using var dock = new TestDock();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Sources.ReadUsages = async ct =>
        {
            using var registration = ct.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Array.Empty<AiUsage>();
        };
        await dock.Start(includeUsages: false);
        var token = Assert.Single(dock.Sources.UsageReads);
        await Bounded(dock.Service.DisposeAsync().AsTask());
        await Bounded(cancelled.Task);
        Assert.True(token.IsCancellationRequested);
        int commits = dock.Surface.Commits;
        dock.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Single(dock.Sources.UsageReads);
        Assert.Equal(commits, dock.Surface.Commits);
    }

    [Fact]
    public async Task Quotas_ImageModifiee_ContenuIdentique_Renvoyee()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        int displays = dock.Surface.Displays.Count;
        dock.Renderer.DisplayVersion++;
        await dock.Advance(60000);
        Assert.Equal(displays + 3, dock.Surface.Displays.Count);
        await dock.AssertDisplays();
    }

    [Fact]
    public async Task Quotas_Rafraichissement_NInvalidePasUnAppui()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        await dock.Click(0);
        dock.Surface.Press(1);
        await dock.Idle();
        dock.Sources.Usages = [Usage(20)];
        await dock.Advance(60000);
        dock.Surface.Release(1);
        await dock.Idle();
        Assert.Equal("Tile 0", Assert.Single(dock.Sources.Runs).Name);
    }

    [Fact]
    public async Task Quotas_Reconnexion_IgnoreLaLectureDeLAncienneSurface()
    {
        await using var dock = new TestDock();
        var read = NewRead();
        dock.Sources.ReadUsages = _ => read.Task;
        await dock.Start(includeUsages: false);
        var oldToken = Assert.Single(dock.Sources.UsageReads);
        dock.Sources.ReadUsages = null;
        dock.Sources.Usages = [Usage(30)];
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        dock.Surface.Disconnect();
        await dock.Idle();
        int displays = replacement.Displays.Count;
        read.SetResult([Usage(90)]);
        await dock.Idle();
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal(displays, replacement.Displays.Count);
        Assert.Equal(2, dock.Sources.UsageReads.Count);
        await dock.AssertDisplays(replacement);
    }

    [Fact]
    public async Task Quotas_Desactivation_EffaceLesEcransSansLectureSupplementaire()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        dock.Sources.Config.Enabled = false;
        await dock.Advance(60000);
        Assert.Single(dock.Sources.UsageReads);
        await dock.AssertDisplays();
    }

    [Fact]
    public async Task Quotas_Publication_SerialiseeAvecLesTouches()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        await dock.Click(0);
        int keys = dock.Surface.Images.Count;
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Surface.CommitGate = commit;
        dock.Surface.CommitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Sources.Usages = [Usage(20)];
        dock.Time.Advance(TimeSpan.FromMinutes(1));
        await Bounded(dock.Surface.CommitStarted.Task);
        dock.Surface.Press(1);
        dock.Surface.Release(1);
        Assert.Equal(keys, dock.Surface.Images.Count);
        Assert.Empty(dock.Sources.Runs);
        commit.SetResult();
        await dock.Idle();
        Assert.Equal(keys + 2, dock.Surface.Images.Count);
        Assert.Single(dock.Sources.Runs);
    }

    [Fact]
    public async Task Quotas_RenduDeTouchesRetarde_NEffacePasLesQuotas()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        var rendering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Renderer.Gate = rendering;
        dock.Service.Start();
        await Bounded(dock.Renderer.Started.Task);
        await Bounded(dock.Surface.CommitStarted.Task);
        rendering.SetResult();
        await dock.Idle();
        Assert.Equal(3, dock.Surface.Displays.Count);
        Assert.Equal(15, dock.Surface.Images.Count);
        await dock.AssertDisplays();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Synchro_ActiveeEnCours_AligneLeDock(int page)
    {
        await using var dock = new TestDock();
        await dock.Start();
        await dock.Click(0);
        dock.Sources.ShortcutPage = page;
        dock.Service.SyncPages = true;
        await dock.Idle();
        DockScreen expected = page == 2 ? new DockScreen.Page(2, 0) : new DockScreen.Page(0, 0);
        Assert.Equal(expected, dock.Service.Screen);
        Assert.Empty(dock.Sources.ShownPages);
        int renders = dock.Renderer.Keys.Count;
        dock.Service.SyncPages = true;
        await dock.Idle();
        Assert.Equal(renders, dock.Renderer.Keys.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Synchro_PageDockSourcesIllisibles_DockPadNeBougePas(bool pages)
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        await dock.Start();
        var failure = new InvalidOperationException("Fake source failed.");
        if (pages) dock.Sources.PagesFailure = failure;
        else dock.Sources.EntriesFailure = failure;
        await dock.Click(1);
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
        Assert.Empty(dock.Sources.ShownPages);
        Assert.Equal(99, dock.Sources.ShortcutPage);
        dock.Sources.PagesFailure = null;
        dock.Sources.EntriesFailure = null;
        await dock.Click(1);
        Assert.Equal(new DockScreen.Page(2, 0), dock.Service.Screen);
        Assert.Equal(2, Assert.Single(dock.Sources.ShownPages));
    }

    [Fact]
    public async Task Synchro_PageDock_AttendLaPublicationAvantDeNotifierDockPad()
    {
        await using var dock = new TestDock();
        dock.Service.SyncPages = true;
        await dock.Start();
        var rendering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Renderer.Gate = rendering;
        dock.Renderer.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Surface.Press(1);
        dock.Surface.Release(1);
        await Bounded(dock.Renderer.Started.Task);
        Assert.Empty(dock.Sources.ShownPages);
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dock.Surface.CommitGate = commit;
        dock.Surface.CommitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rendering.SetResult();
        await Bounded(dock.Surface.CommitStarted.Task);
        Assert.Empty(dock.Sources.ShownPages);
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
        commit.SetResult();
        await dock.Idle();
        Assert.Equal(new DockScreen.Page(2, 0), dock.Service.Screen);
        Assert.Equal(2, Assert.Single(dock.Sources.ShownPages));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Quotas_BandeauDesactiveEnCours_LectureAnnuleeEtEcransNoirs(bool ignoreAnnulation)
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        var read = NewRead();
        dock.Sources.ReadUsages = ct => ignoreAnnulation ? read.Task : read.Task.WaitAsync(ct);
        await dock.Advance(60000, includeUsages: false);
        var token = dock.Sources.UsageReads.Last();
        dock.Sources.Config.Enabled = false;
        dock.Service.OnUsageConfigChanged();
        await dock.Idle();
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(2, dock.Sources.UsageReads.Count);
        await dock.AssertDisplays();
        int displays = dock.Surface.Displays.Count;
        int commits = dock.Surface.Commits;
        int renders = dock.Renderer.Displays.Count;
        read.SetResult([Usage(90)]);
        await dock.Advance(60000);
        Assert.Equal(displays, dock.Surface.Displays.Count);
        Assert.Equal(commits, dock.Surface.Commits);
        Assert.Equal(renders, dock.Renderer.Displays.Count);
        Assert.Equal(2, dock.Sources.UsageReads.Count);
        Assert.DoesNotContain(dock.Logs, line => line.Cause != null);
        dock.Sources.ReadUsages = null;
        dock.Sources.Config.Enabled = true;
        dock.Sources.Usages = [Usage(30)];
        dock.Service.OnUsageConfigChanged();
        await dock.Idle();
        Assert.Equal(3, dock.Sources.UsageReads.Count);
        await dock.AssertDisplays();
    }

    [Fact]
    public async Task Quotas_ConfigModifieeEnCours_RelanceSansAttendreLaLecturePrecedente()
    {
        await using var dock = new TestDock();
        dock.Sources.Usages = [Usage(10)];
        await dock.Start();
        var previousRead = NewRead();
        dock.Sources.ReadUsages = _ => previousRead.Task;
        await dock.Advance(60000, includeUsages: false);
        var previousToken = dock.Sources.UsageReads.Last();
        var currentRead = NewRead();
        dock.Sources.ReadUsages = ct => currentRead.Task.WaitAsync(ct);
        dock.Service.OnUsageConfigChanged();
        await dock.Idle(includeUsages: false);
        Assert.True(previousToken.IsCancellationRequested);
        Assert.Equal(3, dock.Sources.UsageReads.Count);
        int displays = dock.Surface.Displays.Count;
        previousRead.SetResult([Usage(90)]);
        await dock.Advance(60000, includeUsages: false);
        Assert.Equal(3, dock.Sources.UsageReads.Count);
        Assert.Equal(displays, dock.Surface.Displays.Count);
        currentRead.SetResult([Usage(30)]);
        await dock.Idle();
        dock.Sources.Usages = [Usage(30)];
        await dock.AssertDisplays();
        Assert.DoesNotContain(dock.Logs, line => line.Cause != null);
    }

    private static ShortcutEntry Tile(int index, int page = 0) => new()
    {
        Page = page, Row = index / 6, Col = index % 6, Name = $"Tile {index}", Command = $"sample{index}.exe",
    };

    private static AiUsage Usage(int used) => new()
    {
        ProviderId = "claude", Name = "Claude", Glyph = "C", AccentColor = "#FFFFFF",
        Session = new UsageWindow { UsedPct = used }, Week = new UsageWindow { UsedPct = 40 },
    };

    private static TaskCompletionSource<IReadOnlyList<AiUsage>> NewRead() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task Bounded(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5));

    private sealed class TestDock : IAsyncDisposable
    {
        public ManualTimeProvider Time { get; } = new();
        public FakeSurfaceDriver Driver { get; } = new();
        public FakeSurface Surface { get; }
        public FakeDockSources Sources { get; } = new() { Tiles = [Tile(0), Tile(0, page: 2)], ShortcutPage = 99 };
        public FakeDockRenderer Renderer { get; } = new();
        public StreamDockService Service { get; }
        public ConcurrentQueue<(string Message, Exception? Cause)> Logs { get; } = new();

        public TestDock()
        {
            Surface = Driver.Surface ?? throw new InvalidOperationException("No fake surface.");
            Service = new StreamDockService(Driver, Sources, Renderer, Time, (message, cause) => Logs.Enqueue((message, cause)));
        }

        public async Task Start(bool includeUsages = true)
        {
            Service.Start();
            await Idle(includeUsages);
        }

        public Task Idle(bool includeUsages = true) => Bounded(Service.WhenIdleAsync(includeUsages));

        public async Task Click(int index, bool includeUsages = true)
        {
            Surface.Press(index);
            Surface.Release(index);
            await Idle(includeUsages);
        }

        public async Task Advance(int milliseconds, bool includeUsages = true)
        {
            Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            await Idle(includeUsages);
        }

        public async Task AssertDisplays(FakeSurface? surface = null)
        {
            surface ??= Surface;
            var planned = UsageDisplayPlanner.Plan(Sources.Usages, Sources.Config, surface.Description.DisplayCount);
            for (int index = 0; index < planned.Count; index++)
            {
                var expected = await Renderer.RenderDisplayAsync(planned[index], surface.Description.DisplayImageSize);
                var actual = surface.Displays.Where(display => display.Display == index).Last().Image;
                Assert.Equal(expected.Bgra32, actual.Bgra32);
            }
        }

        public ValueTask DisposeAsync() => new(Bounded(Service.DisposeAsync().AsTask()));
    }
}
