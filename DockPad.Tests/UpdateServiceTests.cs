using System.IO;
using DockPad.Services.Updates;

namespace DockPad.Tests;

public class UpdateServiceTests
{
    private sealed class Backend : IUpdateBackend
    {
        public bool IsInstalled { get; init; } = true;
        public string CurrentVersion => "1.0.0";
        public string ContentDirectory => Path.GetTempPath();
        public int Checks, Downloads, Applies;
        public bool FailCheck, FailDownload, FailApply;
        public UpdateRelease? Release = new("2.0.0", "Release notes", new object());
        public TaskCompletionSource? DownloadStarted, FinishDownload;
        public Task<UpdateRelease?> CheckAsync()
        {
            Checks++;
            return FailCheck ? Task.FromException<UpdateRelease?>(new IOException("offline")) : Task.FromResult(Release);
        }
        public async Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token)
        {
            Downloads++; DownloadStarted?.TrySetResult();
            if (FinishDownload is not null) await FinishDownload.Task.WaitAsync(token);
            if (FailDownload) throw new IOException("network interrupted");
            progress(100);
        }
        public void Apply(UpdateRelease release) { Applies++; if (FailApply) throw new IOException("updater unavailable"); }
    }

    [Fact]
    public async Task LegacyInstallationDoesNotCheckOrApply()
    {
        var backend = new Backend { IsInstalled = false };
        using var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.False(await service.DownloadAsync());
        Assert.Equal(UpdateState.Unavailable, service.State);
        Assert.Equal(0, backend.Checks);
        Assert.Throws<InvalidOperationException>(service.Apply);
    }
    [Fact]
    public async Task SuccessfulUpdateRequiresDownloadBeforeApply()
    {
        var backend = new Backend();
        using var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Equal(UpdateState.Available, service.State);
        Assert.Equal("Release notes", service.Notes);
        Assert.Throws<InvalidOperationException>(service.Apply);
        Assert.True(await service.DownloadAsync());
        await service.CheckAsync(); // Keep a downloaded release instead of replacing it beneath Apply.
        Assert.Equal(1, backend.Checks);
        service.Apply();
        Assert.Equal(UpdateState.Installing, service.State);
        Assert.False(service.CanInstall);
        Assert.Equal(1, backend.Applies);
    }
    [Fact]
    public async Task InterruptedDownloadCanBeRetried()
    {
        var backend = new Backend { FailDownload = true };
        using var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.False(await service.DownloadAsync());
        Assert.Equal(UpdateState.Failed, service.State);
        Assert.True(service.CanInstall);
        backend.FailDownload = false;
        Assert.True(await service.DownloadAsync());
        Assert.Equal(2, backend.Downloads);
    }
    [Fact]
    public async Task CancellationAndConcurrentClicksDoNotLaunchUpdater()
    {
        var backend = new Backend { DownloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), FinishDownload = new() };
        using var service = new UpdateService(backend);
        await service.CheckAsync();
        var download = service.DownloadAsync();
        await backend.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await service.DownloadAsync());
        await service.CheckAsync();
        service.CancelDownload();
        Assert.False(await download.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(UpdateState.Available, service.State);
        Assert.Equal(1, backend.Downloads);
        Assert.Equal(1, backend.Checks);
        Assert.Equal(0, backend.Applies);
    }
    [Fact]
    public async Task FailedCheckAndApplyKeepRetryAvailable()
    {
        var backend = new Backend { FailCheck = true };
        using var service = new UpdateService(backend);
        await service.CheckAsync();
        Assert.Equal(UpdateState.Failed, service.State);
        backend.FailCheck = false;
        await service.CheckAsync();
        Assert.True(await service.DownloadAsync());
        backend.FailApply = true;
        Assert.Throws<IOException>(service.Apply);
        Assert.Equal(UpdateState.Ready, service.State);
        backend.FailApply = false;
        service.Apply();
        Assert.Equal(1, backend.Downloads);
    }
    [Fact]
    public async Task NoUpdateReturnsToIdle()
    {
        using var service = new UpdateService(new Backend { Release = null });
        await service.CheckAsync();
        Assert.Equal(UpdateState.Idle, service.State);
        Assert.False(service.HasRelease);
        Assert.False(service.CanInstall);
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
    public async Task PendingWritesWaitsForAnAlreadyClosedDialog()
    {
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingWrites.Track(writing.Task);
        var drain = PendingWrites.CompleteAsync();
        Assert.False(drain.IsCompleted);
        writing.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
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
            var blocker = Assert.Single(UpdateBlockers.Find(directory), b => b.Pid == child.Id);
            Assert.False(await UpdateBlockers.CloseAsync(blocker, force: false));
            Assert.False(child.HasExited);
            Assert.True(await UpdateBlockers.CloseAsync(blocker, force: true));
            Assert.Empty(UpdateBlockers.Find(directory));
        }
        finally
        {
            if (!child.HasExited) { child.StandardInput.WriteLine(); child.StandardInput.Flush(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            Directory.Delete(directory, true);
        }
    }
    [Fact]
    public async Task ReusedPidIsNeverClosed()
    {
        // Even force=true must not touch the current test process for a stale PID identity.
        Assert.True(await UpdateBlockers.CloseAsync(new UpdateBlocker { Pid = Environment.ProcessId, Started = 0 }, force: true));
    }
}
