using DockPad.Services.UpdateHost;
using System.IO;
using DockPad.Services.Localization;
using Mue;

namespace DockPad.Tests;

public class UpdateServiceTests
{
    private sealed class Backend : IUpdateBackend
    {
        public bool IsInstalled => true;
        public string CurrentVersion => "1.0.0";
        public string ContentDirectory => Path.GetTempPath();
        public Task<UpdateRelease?> CheckAsync() =>
            Task.FromResult<UpdateRelease?>(new("2.0.0", "Release notes", new object()));
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token)
        {
            progress(100);
            return Task.CompletedTask;
        }
        public void Apply(UpdateRelease release) { }
    }
    [Theory]
    [InlineData(@"C:\app\current\profile", @"C:\APP\current", true)]
    [InlineData(@"C:\app\current", @"C:\app\current\", true)]
    [InlineData(@"C:\app\current-old\profile", @"C:\app\current", false)]
    [InlineData(@"C:\app\current\..\profile", @"C:\app\current", false)]
    public void ProfileGuardUsesDirectoryBoundaries(string path, string directory, bool expected) =>
        Assert.Equal(expected, AppInstallation.IsWithin(path, directory));
    [Fact]
    public void StablePathOnlyResolvesRecognizedInstallations()
    {
        var root = Path.Combine(Path.GetTempPath(), "dockpad-path-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "current"));
        try
        {
            var exe = Path.Combine(root, "current", "DockPad.exe");
            var stable = Path.Combine(root, "DockPad.exe");
            Assert.Equal(exe, AppInstallation.ResolveExecutable(exe));
            File.WriteAllText(Path.Combine(root, "current", "sq.version"), "fixture");
            File.WriteAllText(Path.Combine(root, "Update.exe"), "fixture");
            File.WriteAllText(stable, "fixture");
            Assert.Equal(stable, AppInstallation.ResolveExecutable(exe));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AcceptedLinksAreForwardedExactlyOnceAtHandoff()
    {
        var scheduled = new Queue<Action>();
        var forwarded = new List<string>();
        var handled = new List<string>();
        var relays = new PendingRelays(scheduled.Enqueue, args => forwarded.Add(args[1]));
        relays.Accept(["--url", "first"], () => handled.Add("first"));
        relays.Accept(["--url", "second"], () => handled.Add("second"));
        scheduled.Dequeue()();
        relays.BeginHandoff();
        relays.Accept(["--url", "third"], () => handled.Add("third"));
        while (scheduled.TryDequeue(out var action)) action();
        Assert.Equal(["first"], handled);
        Assert.Equal(["second", "third"], forwarded);
        relays.Resume();
        relays.Accept(["--url", "fourth"], () => handled.Add("fourth"));
        scheduled.Dequeue()();
        Assert.Equal(["first", "fourth"], handled);
    }
    [Fact]
    public async Task RestartManagerFindsExternalLockAndRequiresExplicitForce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dockpad-lock-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "locked.txt");
        File.WriteAllText(file, "fixture");
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardInput = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$f = [IO.File]::Open('" + file.Replace("'", "''")
            + "', 'Open', 'Read', 'None'); [Console]::WriteLine('ready'); [Console]::ReadLine() | Out-Null; $f.Dispose()");
        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            var blocker = Assert.Single(new WindowsUpdateBlockerFinder().Find(directory, new DockPadBlockerClassifier()), b => b.Pid == child.Id);
            Assert.False(await new WindowsUpdateBlockerFinder().CloseAsync(blocker, force: false));
            Assert.False(child.HasExited);
            Assert.True(await new WindowsUpdateBlockerFinder().CloseAsync(blocker, force: true));
            Assert.Empty(new WindowsUpdateBlockerFinder().Find(directory, new DockPadBlockerClassifier()));
        }
        finally
        {
            if (!child.HasExited) { child.StandardInput.WriteLine(); child.StandardInput.Flush(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Préparation après téléchargement : état occupé, indicateur indéterminé, abandon qui rend la
    /// main, application possible depuis la préparation, préparation refusée avant téléchargement.
    /// </summary>
    [Fact]
    public async Task PreparationShowsAnIndeterminateBusyState()
    {
        using var service = new UpdateService(new Backend());
        await service.CheckAsync();
        Assert.True(await service.DownloadAsync());

        service.BeginPreparation();

        Assert.Equal(UpdateState.Preparing, service.State);
        Assert.True(service.Busy);
        Assert.False(service.CanInstall);
        Assert.False(service.CanCheck);
        Assert.True(service.IsIndeterminate);
        Assert.True(service.ShowsProgress);
        Assert.Equal(Loc.T("Update_Preparing"), service.Status);
    }

}
