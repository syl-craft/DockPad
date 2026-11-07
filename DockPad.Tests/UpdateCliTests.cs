using System.Threading;
using System.Threading.Tasks;
using DockPad.Services;
using DockPad.Services.UpdateHost;
using Mue;

namespace DockPad.Tests;

public class UpdateCliTests
{
    [Fact]
    public void OnlyALeadingUpdateVerbRunsTheCommand()
    {
        Assert.True(UpdateCli.Handles(["update", "--json"]));
        Assert.False(UpdateCli.Handles([]));
        Assert.False(UpdateCli.Handles(["--url", "update"]));
        Assert.False(UpdateCli.Handles(["Update"]));
    }

    // Une demande de quitter née d'un argument de lancement fermerait DockPad sur un simple clic.
    [Theory]
    [InlineData("quit-for-update")]
    [InlineData("update")]
    [InlineData("--close-blockers")]
    public void NoLaunchArgumentAsksTheResidentToQuit(string arg)
    {
        var plan = StartupRelay.Plan([arg]);
        Assert.False(StartupRelay.IsAnyQuitRequest(plan?.Payload ?? ""));
    }

    // Une instance qui aurait repris le tube après la vérification du PID ignore une demande qui ne la nomme pas.
    [Fact]
    public void AQuitRequestOnlyReachesTheInstanceItNames()
    {
        Assert.True(StartupRelay.IsQuitRequest(StartupRelay.QuitRequest(42), 42));
        Assert.False(StartupRelay.IsQuitRequest(StartupRelay.QuitRequest(42), 43));
        Assert.True(StartupRelay.IsAnyQuitRequest(StartupRelay.QuitRequest(43)));
        Assert.False(StartupRelay.IsAnyQuitRequest(StartupRelay.ShowRequest));
    }

    [Fact]
    public void AStartupErrorStillWritesOneJsonReport()
    {
        var output = new System.IO.StringWriter();
        UpdateCli.WriteStartupError(output, json: true, "profil illisible");
        using var report = System.Text.Json.JsonDocument.Parse(output.ToString());
        Assert.Equal("error", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("profil illisible", report.RootElement.GetProperty("message").GetString());
        Assert.Equal(0, report.RootElement.GetProperty("blockers").GetArrayLength());
    }

    [Fact]
    public void OnlyTheResidentInstanceIsClosedThroughItsPipe()
    {
        Assert.True(ResidentClosingFinder.IsResident(new UpdateBlocker { Name = "DockPad", Pid = 1, Started = 1 }));
        Assert.False(ResidentClosingFinder.IsResident(new UpdateBlocker { Name = "DockPad", Pid = 1, Started = 1, IsRelay = true }));
        Assert.False(ResidentClosingFinder.IsResident(new UpdateBlocker { Name = "DockPad", Pid = 1, Unavailable = true }));
        Assert.False(ResidentClosingFinder.IsResident(new UpdateBlocker { Name = "Code", Pid = 1, Started = 1 }));
    }

    [Fact]
    public async Task OtherProgramsAndForcedClosesGoToMue()
    {
        var inner = new RecordingFinder();
        var finder = new ResidentClosingFinder(inner);

        await finder.CloseAsync(new UpdateBlocker { Name = "Code", Pid = 7, Started = 1 }, force: false);
        await finder.CloseAsync(new UpdateBlocker { Name = "DockPad", Pid = 8, Started = 1 }, force: true);

        Assert.Equal([(7, false), (8, true)], inner.Closed);
    }

    // Sans relancement, personne ne retirerait le marqueur : le lanceur stable retiendrait le prochain lancement.
    [Fact]
    public void TheUpdateMarkerIsOnlySetWhenDockPadRestarts()
    {
        new DockPadCommandHost(restart: false).BeforeApply();
        // Hors installation Velopack, poser le marqueur lève : la preuve que la branche est prise.
        Assert.Throws<InvalidOperationException>(() => new DockPadCommandHost(restart: true).BeforeApply());
    }

    private sealed class RecordingFinder : IUpdateBlockerFinder
    {
        public List<(int Pid, bool Force)> Closed { get; } = [];
        public UpdateBlockerScan Find(UpdateBlockerQuery query, IUpdateBlockerClassifier classifier, bool enrich = true) => new([]);
        public Task<UpdateBlockerCloseResult> CloseAsync(UpdateBlocker blocker, bool force, CancellationToken token = default)
        {
            Closed.Add((blocker.Pid, force));
            return Task.FromResult(UpdateBlockerCloseResult.Closed);
        }
    }
}
