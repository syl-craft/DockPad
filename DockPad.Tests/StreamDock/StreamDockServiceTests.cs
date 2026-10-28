using System.Collections.Concurrent;
using DockPad.Models;
using DockPad.Services.StreamDock;

namespace DockPad.Tests;

/// <summary>
/// Connexion et reprise, rendu différentiel, appuis croisés ou répétés, navigation et invalidation pendant un redessin.
/// Journaux dédupliqués, sources défaillantes, rafales bornées, fermeture avant reconnexion et arrêt bloqué.
/// </summary>
public class StreamDockServiceTests
{
    [Fact]
    public async Task Demarrage_AfficheLAccueil()
    {
        await using var dock = new TestDock();
        dock.Surface.BeforeImage = () => Assert.Equal(30, dock.Renderer.Keys.Count);
        await dock.Start();
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
        Assert.Equal(15, dock.Surface.Images.Count);
        Assert.Equal(2, dock.Surface.Commits);
        Assert.IsType<DockKey.OpenPage>(dock.Renderer.Keys.First().Key);
    }

    [Fact]
    public async Task StyleDesTouches_NeonParDefautPuisToutesLesTouchesRedessineesSurLaPageOuverte()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        Assert.All(dock.Renderer.KeyStyles, style => Assert.Equal(DockKeyStyle.Neon, style));
        var screen = dock.Service.Screen;
        int renders = dock.Renderer.Keys.Count;
        int images = dock.Surface.Images.Count;

        dock.Service.KeyStyle = DockKeyStyle.Flat;
        await dock.Idle();
        Assert.Equal(screen, dock.Service.Screen);
        Assert.Equal(30, dock.Renderer.Keys.Count - renders);
        Assert.All(dock.Renderer.KeyStyles.Skip(renders), style => Assert.Equal(DockKeyStyle.Flat, style));
        Assert.Equal(images + 15, dock.Surface.Images.Count);

        renders = dock.Renderer.Keys.Count;
        dock.Service.KeyStyle = DockKeyStyle.Flat;
        await dock.Idle();
        Assert.Equal(renders, dock.Renderer.Keys.Count);
    }

    [Fact]
    public async Task SansSurface_ReessaieToutesLes5Secondes()
    {
        await using var dock = new TestDock();
        dock.Driver.Surface = null;
        await dock.Start();
        Assert.Equal(1, dock.Driver.Searches);
        await dock.Advance(4999);
        Assert.Equal(1, dock.Driver.Searches);
        await dock.Advance(1);
        Assert.Equal(2, dock.Driver.Searches);
        await dock.Advance(5000);
        Assert.Equal(3, dock.Driver.Searches);
        dock.Driver.Surface = dock.Surface;
        dock.Driver.Changed();
        await dock.Idle();
        Assert.Equal(2, dock.Surface.Commits);
    }

    [Fact]
    public async Task SurfaceOccupee_ReessaieSansRejournaliser()
    {
        await using var dock = new TestDock();
        dock.Driver.Busy = true;
        await dock.Start();
        await dock.Advance(5000);
        await dock.Advance(5000);
        Assert.Equal(3, dock.Driver.Opens);
        Assert.Equal("Stream Dock : occupé.", Assert.Single(dock.Logs).Message);
        Assert.Equal(0, dock.Surface.Commits);
        dock.Driver.Busy = false;
        dock.Driver.Changed();
        await dock.Idle();
        Assert.Equal(4, dock.Driver.Opens);
        Assert.Equal(2, dock.Surface.Commits);
    }

    [Fact]
    public async Task Appui_ImageEnfonceeAussitot_RelachementLanceUneFois()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        var normal = dock.Surface.Images.Where(image => image.Key == FakeSurface.Key(1)).Last().Image;
        int commits = dock.Surface.Commits;
        dock.Surface.Press(1);
        await dock.Idle();
        Assert.Equal(commits + 1, dock.Surface.Commits);
        Assert.False(normal.Bgra32.SequenceEqual(dock.Surface.Images.Last().Image.Bgra32));
        Assert.Empty(dock.Sources.Runs);
        dock.Surface.Release(1);
        dock.Surface.Release(1);
        await dock.Idle();
        Assert.Equal(normal.Bgra32, dock.Surface.Images.Last().Image.Bgra32);
        Assert.Equal("Tile 0", Assert.Single(dock.Sources.Runs).Name);
        Assert.Equal(commits + 2, dock.Surface.Commits);
    }

    [Fact]
    public async Task MaintenueEtAppuisRepetes_UnSeulLancement()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        int commits = dock.Surface.Commits;
        dock.Surface.Press(1);
        dock.Surface.Press(1);
        await dock.Advance(10000);
        Assert.Empty(dock.Sources.Runs);
        Assert.Equal(commits + 1, dock.Surface.Commits);
        dock.Surface.Release(1);
        dock.Surface.Release(1);
        await dock.Idle();
        Assert.Single(dock.Sources.Runs);
    }

    [Fact]
    public async Task ContenuChangeEntreAppuiEtRelachement_RienNEstLance()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        dock.Surface.Press(1);
        await dock.Idle();
        dock.Sources.Tiles[0].Name = "Changed";
        dock.Service.OnGridChanged();
        await dock.Advance(30);
        dock.Surface.Press(1);
        dock.Surface.Release(1);
        await dock.Idle();
        Assert.Empty(dock.Sources.Runs);
        await dock.Click(1);
        Assert.Equal("Changed", Assert.Single(dock.Sources.Runs).Name);
    }

    [Fact]
    public async Task AppuisCroises_ChaqueToucheLanceSaPropreCible()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        dock.Surface.Press(1);
        dock.Surface.Press(2);
        dock.Surface.Release(2);
        dock.Surface.Release(1);
        await dock.Idle();
        Assert.Equal(new[] { "Tile 1", "Tile 0" }, dock.Sources.Runs.Select(entry => entry.Name));
    }

    [Fact]
    public async Task OuvrirPage_PuisAccueil()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        await dock.Click(0);
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
        Assert.Empty(dock.Sources.Runs);
    }

    [Fact]
    public async Task Groupe_RetourALaMemeFeuille()
    {
        await using var dock = new TestDock();
        var entries = Enumerable.Range(0, 15).Select(Tile).ToArray();
        entries[14].Layout = TileLayout.Quad;
        entries[14].Children = [Tile(0)];
        dock.Sources.Tiles = entries;
        await dock.OpenPage();
        await dock.Click(14);
        Assert.Equal(new DockScreen.Page(0, 1), dock.Service.Screen);
        await dock.Click(2);
        Assert.Equal(new DockScreen.Group(0, 2, 2, 1, 0), dock.Service.Screen);
        await dock.Click(0);
        Assert.Equal(new DockScreen.Page(0, 1), dock.Service.Screen);
    }

    [Fact]
    public async Task Suite_Cycle()
    {
        await using var dock = new TestDock();
        dock.Sources.Tiles = Enumerable.Range(0, 16).Select(index => new ShortcutEntry { Page = index, Name = "Tile" }).ToArray();
        await dock.Start();
        await dock.Click(14);
        Assert.Equal(new DockScreen.Home(1), dock.Service.Screen);
        await dock.Click(14);
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
    }

    [Fact]
    public async Task Redessin_SeulesLesTouchesModifieesSontRenvoyees()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        int images = dock.Surface.Images.Count;
        int commits = dock.Surface.Commits;
        int renders = dock.Renderer.Keys.Count;
        // Une nouvelle instance identique ne doit pas provoquer un envoi.
        dock.Sources.Tiles = [Tile(0), Tile(1)];
        dock.Sources.Tiles[0].Name = "Changed";
        for (int index = 0; index < 20; index++) dock.Service.OnGridChanged();
        await dock.Advance(29);
        Assert.Equal(renders, dock.Renderer.Keys.Count);
        await dock.Advance(1);
        Assert.Equal(renders + 30, dock.Renderer.Keys.Count);
        Assert.Equal(images + 1, dock.Surface.Images.Count);
        Assert.Equal(FakeSurface.Key(1), dock.Surface.Images.Last().Key);
        Assert.Equal(commits + 1, dock.Surface.Commits);
    }

    [Fact]
    public async Task Debranchement_ReprisSurLAccueil()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        dock.Surface.Press(1);
        await dock.Idle();
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        dock.Surface.Disconnect();
        dock.Surface.Release(1);
        dock.Driver.Changed();
        await dock.Idle();
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
        Assert.Equal(2, replacement.Commits);
        Assert.Empty(dock.Sources.Runs);
    }

    [Fact]
    public async Task DebranchementPendantUneEcriture_PasDeBlocage()
    {
        await using var dock = new TestDock();
        var gate = NewGate();
        dock.Surface.WriteGate = gate;
        dock.Service.Start();
        await Bounded(dock.Surface.WriteStarted.Task);
        dock.Driver.Surface = new FakeSurface();
        var failure = new System.IO.IOException("Disconnected during write.");
        dock.Surface.Disconnect(failure);
        await dock.Idle();
        Assert.Equal(2, dock.Driver.Surface.Commits);
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        gate.TrySetResult();
    }

    [Fact]
    public async Task Arret_RendLaMainSiLaSurfaceBloque()
    {
        await using var dock = new TestDock();
        var writeGate = NewGate();
        var closeGate = NewGate();
        dock.Surface.WriteGate = writeGate;
        dock.Surface.DisposeGate = closeGate;
        dock.Service.Start();
        await Bounded(dock.Surface.WriteStarted.Task);
        var stopping = dock.Service.DisposeAsync().AsTask();
        await Bounded(dock.Surface.DisposeStarted.Task);
        Assert.False(stopping.IsCompleted);
        dock.Time.Advance(TimeSpan.FromSeconds(1));
        await Bounded(stopping);
        await Bounded(dock.Service.DisposeAsync().AsTask());
        Assert.Equal(1, dock.Surface.DisposeCalls);
        int searches = dock.Driver.Searches;
        dock.Time.Advance(TimeSpan.FromSeconds(20));
        dock.Driver.Changed();
        Assert.Equal(searches, dock.Driver.Searches);
        writeGate.TrySetResult();
        closeGate.TrySetResult();
    }

    [Fact]
    public async Task Arret_FermeLaSurface()
    {
        await using var dock = new TestDock();
        await dock.Start();
        await Bounded(dock.Service.DisposeAsync().AsTask());
        await Bounded(dock.Service.DisposeAsync().AsTask());
        Assert.Equal(1, dock.Surface.DisposeCalls);
    }

    [Fact]
    public async Task AppuiPendantCommit_NeLancePasLaNouvelleCible()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        var gate = NewGate();
        dock.Surface.CommitGate = gate;
        dock.Surface.CommitStarted = NewGate();
        dock.Sources.Tiles[0].Name = "Changed";
        dock.Service.OnGridChanged();
        dock.Time.Advance(TimeSpan.FromMilliseconds(30));
        await Bounded(dock.Surface.CommitStarted.Task);
        dock.Surface.Press(1);
        gate.TrySetResult();
        await dock.Idle();
        dock.Surface.Release(1);
        await dock.Idle();
        Assert.Empty(dock.Sources.Runs);
    }

    [Fact]
    public async Task RedessinDepasse_AucunEnvoiDuRenduPerime()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        int images = dock.Surface.Images.Count;
        var gate = NewGate();
        dock.Renderer.Gate = gate;
        dock.Renderer.Started = NewGate();
        dock.Sources.Tiles[0].Name = "Intermediate";
        dock.Service.OnGridChanged();
        dock.Time.Advance(TimeSpan.FromMilliseconds(30));
        await Bounded(dock.Renderer.Started.Task);
        dock.Sources.Tiles[0].Name = "Final";
        dock.Service.OnGridChanged();
        gate.TrySetResult();
        await dock.Idle();
        Assert.Equal(images, dock.Surface.Images.Count);
        await dock.Advance(30);
        Assert.Equal(images + 1, dock.Surface.Images.Count);
        await dock.Click(1);
        Assert.Equal("Final", Assert.Single(dock.Sources.Runs).Name);
    }

    [Fact]
    public async Task ToucheVide_OuRelachementSansAppui_SansEffet()
    {
        await using var dock = new TestDock();
        await dock.Start();
        dock.Surface.Press(14);
        dock.Surface.Release(14);
        dock.Surface.Release(0);
        await dock.Idle();
        Assert.Equal(2, dock.Surface.Commits);
        Assert.Empty(dock.Sources.Runs);
    }

    [Fact]
    public async Task EchecDEcriture_ReprendSansLancerLaTuile()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        dock.Surface.WriteFailure = new System.IO.IOException("Fake write failed.");
        dock.Surface.Press(1);
        dock.Surface.Release(1);
        await dock.Idle();
        dock.Driver.Surface = new FakeSurface();
        await dock.Advance(5000);
        Assert.Equal(2, dock.Driver.Surface.Commits);
        Assert.Empty(dock.Sources.Runs);
    }

    [Fact]
    public async Task AppuisCroisesPendantRedessin_LancentLesCiblesEncoreAffichees()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        dock.Surface.Press(1);
        await dock.Idle();
        var gate = NewGate();
        dock.Renderer.Gate = gate;
        dock.Renderer.Started = NewGate();
        dock.Sources.Tiles[0].Command = "changed.exe";
        dock.Service.OnGridChanged();
        dock.Time.Advance(TimeSpan.FromMilliseconds(30));
        await Bounded(dock.Renderer.Started.Task);
        var released = NewGate();
        dock.Sources.AfterRun = () =>
        {
            if (dock.Sources.Runs.Count == 2) released.TrySetResult();
        };
        dock.Surface.Press(2);
        dock.Surface.Release(1);
        dock.Surface.Release(2);
        await Bounded(released.Task);
        Assert.Equal(new[] { "sample0.exe", "sample1.exe" }, dock.Sources.Runs.Select(entry => entry.Command));
        gate.TrySetResult();
        await dock.Idle();
        dock.Sources.Runs.Clear();
        await dock.Click(2);
        await dock.Click(1);
        Assert.Equal(new[] { "sample1.exe", "changed.exe" }, dock.Sources.Runs.Select(entry => entry.Command));
    }

    [Fact]
    public async Task Relachement_AttendLeCommitAvantDeLancer()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        dock.Surface.Press(1);
        await dock.Idle();
        var gate = NewGate();
        dock.Surface.CommitGate = gate;
        dock.Surface.CommitStarted = NewGate();
        dock.Surface.Release(1);
        await Bounded(dock.Surface.CommitStarted.Task);
        Assert.Empty(dock.Sources.Runs);
        gate.TrySetResult();
        await dock.Idle();
        Assert.Single(dock.Sources.Runs);
    }

    [Fact]
    public async Task PremierCommitEnCours_AppuisIgnores()
    {
        await using var dock = new TestDock();
        var gate = NewGate();
        dock.Surface.CommitGate = gate;
        dock.Service.Start();
        await Bounded(dock.Surface.CommitStarted.Task);
        dock.Surface.Press(0);
        dock.Surface.Release(0);
        gate.TrySetResult();
        await dock.Idle();
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
        Assert.Equal(2, dock.Surface.Commits);
    }

    [Fact]
    public async Task Redessin_IconeEtEnfantsModifiesSontRenvoyes()
    {
        await using var dock = new TestDock();
        var group = Tile(1);
        group.Layout = TileLayout.Quad;
        group.Children = [Tile(0)];
        dock.Sources.Tiles = [Tile(0), group];
        await dock.OpenPage();
        int images = dock.Surface.Images.Count;
        dock.Sources.Tiles[0].IconPath = "new-icon.png";
        var child = group.Children[0] ?? throw new InvalidOperationException("No child.");
        child.Name = "Changed child";
        dock.Service.OnGridChanged();
        await dock.Advance(30);
        Assert.Equal(images + 2, dock.Surface.Images.Count);
        Assert.Equal(new[] { FakeSurface.Key(1), FakeSurface.Key(2) }, dock.Surface.Images.Skip(images).Select(image => image.Key));
    }

    [Fact]
    public async Task ArretPendantOuverture_FermeAussiLaSurfaceTardive()
    {
        await using var dock = new TestDock();
        var gate = NewGate();
        dock.Driver.OpenGate = gate;
        dock.Service.Start();
        await Bounded(dock.Driver.OpenStarted.Task);
        await Bounded(dock.Service.DisposeAsync().AsTask());
        gate.TrySetResult();
        await Bounded(dock.Surface.DisposeStarted.Task);
        Assert.Equal(1, dock.Surface.DisposeCalls);
        Assert.Equal(0, dock.Surface.Commits);
    }

    [Fact]
    public async Task DemarrageEtArretRepetes_AucuneDoubleOuverture()
    {
        await using var dock = new TestDock();
        dock.Service.Start();
        dock.Service.Start();
        await dock.Idle();
        Assert.Equal(1, dock.Driver.Opens);
        await Bounded(Task.WhenAll(dock.Service.DisposeAsync().AsTask(), dock.Service.DisposeAsync().AsTask()));
        dock.Service.Start();
        Assert.Equal(1, dock.Driver.Opens);
        Assert.Equal(1, dock.Surface.DisposeCalls);
    }

    [Fact]
    public async Task GrilleVideePendantNavigation_RetourAccueil()
    {
        await using var dock = new TestDock();
        await dock.Start();
        dock.Surface.Press(0);
        await dock.Idle();
        dock.Sources.Tiles = [];
        dock.Service.OnGridChanged();
        dock.Surface.Release(0);
        await dock.Idle();
        await dock.Advance(30);
        Assert.Equal(new DockScreen.Home(0), dock.Service.Screen);
    }

    [Fact]
    public async Task SurfaceDejaDeconnectee_RepriseSurUneNouvelleSurface()
    {
        await using var dock = new TestDock();
        dock.Surface.Disconnect();
        await dock.Start();
        Assert.Equal(0, dock.Surface.Commits);
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        dock.Driver.Changed();
        await dock.Idle();
        Assert.Equal(2, replacement.Commits);
    }

    [Fact]
    public async Task EchecDuRendu_ReprendLaConnexion()
    {
        await using var dock = new TestDock();
        var gate = NewGate();
        dock.Renderer.Gate = gate;
        dock.Service.Start();
        await Bounded(dock.Renderer.Started.Task);
        gate.TrySetException(new InvalidOperationException("Fake rendering failed."));
        await dock.Idle();
        Assert.Empty(dock.Surface.Images);
        dock.Renderer.Gate = null;
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        await dock.Advance(5000);
        Assert.Equal(2, replacement.Commits);
    }

    [Fact]
    public async Task Redessin_LibelleTraduitChange_RenvoyeMemeSiLaToucheEstIdentique()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        int images = dock.Surface.Images.Count;
        var previous = dock.Surface.Images.Where(image => image.Key == FakeSurface.Key(1)).Last().Image;
        dock.Renderer.Version++;
        dock.Service.OnGridChanged();
        await dock.Advance(30);
        Assert.Equal(images + 15, dock.Surface.Images.Count);
        var normal = dock.Surface.Images.Where(image => image.Key == FakeSurface.Key(1)).Last().Image;
        Assert.False(previous.Bgra32.SequenceEqual(normal.Bgra32));
        var tile = new DockKey.RunTile(dock.Sources.Tiles[0]);
        var pressed = await dock.Renderer.RenderKeyAsync(tile, dock.Service.KeyStyle, pressed: true, dock.Surface.Description.KeyImageSize);
        dock.Surface.Press(1);
        await dock.Idle();
        Assert.Equal(pressed.Bgra32, dock.Surface.Images.Last().Image.Bgra32);
        dock.Surface.Release(1);
        await dock.Idle();
        Assert.Equal(normal.Bgra32, dock.Surface.Images.Last().Image.Bgra32);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnexion_AttendLaFermetureOuUneSeconde(bool fermetureBloquee)
    {
        await using var dock = new TestDock();
        await dock.Start();
        var gate = NewGate();
        dock.Surface.DisposeGate = gate;
        var replacement = new FakeSurface();
        dock.Driver.Surface = replacement;
        dock.Surface.Disconnect();
        await Bounded(dock.Surface.DisposeStarted.Task);
        Assert.Equal(1, dock.Driver.Opens);
        dock.Time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, dock.Driver.Opens);
        if (fermetureBloquee) dock.Time.Advance(TimeSpan.FromMilliseconds(1));
        else gate.TrySetResult();
        await dock.Idle();
        Assert.Equal(2, dock.Driver.Opens);
        Assert.Equal(2, replacement.Commits);
        gate.TrySetResult();
    }

    [Fact]
    public async Task PanneRepetee_UneSeuleLigneDeJournal()
    {
        await using var dock = new TestDock();
        var failure = new InvalidOperationException("Fake opening failed.");
        dock.Driver.OpenFailure = failure;
        await dock.Start();
        await dock.Advance(5000);
        await dock.Advance(5000);
        Assert.Equal(3, dock.Driver.Opens);
        Assert.Same(failure, Assert.Single(dock.Logs).Cause);
        dock.Driver.OpenFailure = new System.IO.IOException("Different opening failure.");
        await dock.Advance(5000);
        Assert.Equal(2, dock.Logs.Count);
        dock.Driver.OpenFailure = null;
        await dock.Advance(5000);
        Assert.Equal("Stream Dock : connecté.", dock.Logs.Last().Message);
    }

    [Fact]
    public async Task LancementEnEchec_LeDockResteConnecte()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        var failure = new InvalidOperationException("Fake launch failed.");
        dock.Sources.RunFailure = failure;
        await dock.Click(1);
        await dock.Click(1);
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        Assert.Equal(0, dock.Surface.DisposeCalls);
        Assert.Equal(1, dock.Driver.Opens);
        dock.Sources.RunFailure = null;
        await dock.Click(1);
        Assert.Single(dock.Sources.Runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LectureDesSourcesEnEchec_LeDockResteConnecte(bool pages)
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        var failure = new InvalidOperationException("Fake source failed.");
        if (pages) dock.Sources.PagesFailure = failure;
        else dock.Sources.EntriesFailure = failure;
        dock.Service.OnGridChanged();
        await dock.Advance(30);
        var failures = dock.Logs.Where(line => line.Cause != null).ToArray();
        Assert.Same(failure, Assert.Single(failures).Cause);
        Assert.Equal(new DockScreen.Page(0, 0), dock.Service.Screen);
        Assert.Equal(0, dock.Surface.DisposeCalls);
        Assert.Equal(1, dock.Driver.Opens);
        dock.Sources.PagesFailure = null;
        dock.Sources.EntriesFailure = null;
        dock.Sources.Tiles[0].Name = "Recovered";
        dock.Service.OnGridChanged();
        await dock.Advance(30);
        await dock.Click(1);
        Assert.Equal("Recovered", Assert.Single(dock.Sources.Runs).Name);
    }

    [Fact]
    public async Task RafaleContinue_DessineeAuPlusTardEn200ms()
    {
        await using var dock = new TestDock();
        await dock.OpenPage();
        int commits = dock.Surface.Commits;
        for (int index = 0; index < 20; index++)
        {
            dock.Sources.Tiles[0].Name = $"Change {index}";
            dock.Service.OnGridChanged();
            await dock.Advance(20);
            if (index == 9) Assert.Equal(commits + 1, dock.Surface.Commits);
        }
        Assert.Equal(commits + 2, dock.Surface.Commits);
        await dock.Click(1);
        Assert.Equal("Change 19", Assert.Single(dock.Sources.Runs).Name);
        dock.Service.OnGridChanged();
        await dock.Advance(29);
        int afterClick = dock.Surface.Commits;
        await dock.Advance(1);
        Assert.Equal(afterClick + 1, dock.Surface.Commits);
    }

    private static ShortcutEntry Tile(int index) => new()
    {
        Row = index / 6, Col = index % 6, Name = $"Tile {index}", Command = $"sample{index}.exe",
    };

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Bounded(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5));

    private sealed class TestDock : IAsyncDisposable
    {
        public ManualTimeProvider Time { get; } = new();
        public FakeSurfaceDriver Driver { get; } = new();
        public FakeSurface Surface { get; }
        public FakeDockSources Sources { get; } = new() { Tiles = [Tile(0), Tile(1)] };
        public FakeDockRenderer Renderer { get; } = new();
        public StreamDockService Service { get; }
        public ConcurrentQueue<(string Message, Exception? Cause)> Logs { get; } = new();

        public TestDock()
        {
            Surface = Driver.Surface ?? throw new InvalidOperationException("No fake surface.");
            Service = new StreamDockService(Driver, Sources, Renderer, Time, new FakeSystemLoadMonitor(), (message, cause) => Logs.Enqueue((message, cause)));
        }

        public async Task Start()
        {
            Service.Start();
            await Idle();
        }

        public async Task OpenPage()
        {
            await Start();
            await Click(0);
        }

        public Task Idle() => Bounded(Service.WhenIdleAsync());

        public async Task Click(int index)
        {
            Surface.Press(index);
            Surface.Release(index);
            await Idle();
        }

        public async Task Advance(int milliseconds)
        {
            Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            await Idle();
        }

        public ValueTask DisposeAsync() => new(Bounded(Service.DisposeAsync().AsTask()));
    }
}
