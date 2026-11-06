using Mue.Wpf;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DockPad.Services.Localization;
using Mue;

namespace DockPad.Tests;

public class UpdateHostInjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InstallationKeepsPreparationAndHandoffInOrder(bool failApply) => WpfTestHost.Run(() =>
    {
        var calls = new List<string>();
        var host = new Host(calls);
        using var service = new UpdateService(new Backend(calls, failApply));
        WpfTestHost.PumpUntilCompleted(service.CheckAsync());
        var window = new UpdatesDialog(service, host);
        host.Owner = window;
        try
        {
            ((Button)window.FindName("InstallButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WpfTestHost.PumpUntilCompleted(host.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(new[] { "download", "prepare", "find", "find-final", "prepare", "before", "apply" }
                .Concat(failApply ? ["failed", "error"] : new[] { "exit" }), calls);
            Assert.Equal(failApply ? UpdateState.Ready : UpdateState.Installing, service.State);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task EitherPreparationCanPostponeInstallation(int refusedPreparation) => WpfTestHost.Run(() =>
    {
        var calls = new List<string>();
        var host = new Host(calls) { RefusedPreparation = refusedPreparation };
        using var service = new UpdateService(new Backend(calls, failApply: false));
        WpfTestHost.PumpUntilCompleted(service.CheckAsync());
        var window = new UpdatesDialog(service, host);
        host.Owner = window;
        try
        {
            ((Button)window.FindName("InstallButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WpfTestHost.PumpUntilCompleted(host.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(refusedPreparation == 1 ? new[] { "download", "prepare" }
                : new[] { "download", "prepare", "find", "find-final", "prepare" }, calls);
            Assert.Equal(UpdateState.Ready, service.State);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AutomaticCheckboxWritesTheInjectedSchedule() => WpfTestHost.Run(() =>
    {
        var schedule = new MemoryUpdateSchedule { Automatic = false };
        using var service = new UpdateService(new FakeUpdateBackend(), new() { Schedule = schedule });
        var window = new UpdatesDialog(service, new Host([]));
        try
        {
            var automatic = (CheckBox)window.FindName("Automatic");
            Assert.False(automatic.IsChecked);
            automatic.IsChecked = true;
            automatic.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert.True(schedule.Automatic);
            Assert.True(service.Automatic);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false, false, "Update_BlockedHelp")]
    [InlineData(false, true, "Update_IncompleteHelp")]
    [InlineData(true, false, "Update_BlockedHelpForced")]
    [InlineData(true, true, "Update_IncompleteHelpForced")]
    public Task BlockerHelpFollowsTheShutdownPolicy(bool forced, bool incomplete, string key) => WpfTestHost.Run(() =>
    {
        var previous = Loc.Current;
        var window = new UpdateBlockersDialog("unused", [new() { Pid = 42, Selected = true }],
            new Host([]), forcesShutdown: forced, incomplete: incomplete);
        try
        {
            foreach (var language in new[] { "fr", "en", "qps-Ploc" })
            {
                Loc.SetCulture(CultureInfo.GetCultureInfo(language));
                Assert.Equal(Loc.T(key), ((TextBlock)window.FindName("HelpText")).Text);
                Assert.DoesNotContain("[Update_", ((TextBlock)window.FindName("HelpText")).Text);
            }
        }
        finally { window.Close(); Loc.SetCulture(previous); }
    });

    [Fact]
    public Task BlockerDialogUsesTheHostFinderForClosingAndRefresh() => WpfTestHost.Run(() =>
    {
        var calls = new List<string>();
        var blocker = new UpdateBlocker { Pid = 42, Started = 100, Selected = true };
        var host = new Host(calls) { Blockers = [blocker] };
        var window = new UpdateBlockersDialog("unused", [blocker], host, forcesShutdown: false);
        try
        {
            ((Button)window.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WpfTestHost.PumpUntilCompleted(host.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            WpfTestHost.Flush();
            Assert.Equal(new[] { "close", "find" }, calls);
            Assert.Same(blocker, host.ClosedBlocker);
        }
        finally { window.Close(); }
    });

    private sealed class Host(List<string> calls) : IUpdateDialogHost, IUpdateBlockerFinder
    {
        private int _preparations;
        public object? Owner { get; set; }
        public int RefusedPreparation { get; init; }
        public IReadOnlyList<UpdateBlocker> Blockers { get; init; } = [];
        public UpdateBlocker? ClosedBlocker { get; private set; }
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IUpdateLog Log { get; } = new SilentUpdateLog();
        public IUpdateBlockerClassifier Classifier { get; } = new ProgramClassifier();
        public IUpdateBlockerFinder Finder => this;
        public bool IsExiting => false;
        public string ReleasePage(string version) => "https://example.test/" + version;
        public string LatestReleasePage => "https://example.test/latest";
        public Task<bool> PrepareAsync(object owner, CancellationToken token)
        {
            Assert.Same(Owner, owner);
            calls.Add("prepare");
            var accepted = ++_preparations != RefusedPreparation;
            if (!accepted) Completed.TrySetResult();
            return Task.FromResult(accepted);
        }
        public void BeforeApply() => calls.Add("before");
        public void ApplyFailed() => calls.Add("failed");
        public void ExitAfterApply() { calls.Add("exit"); Completed.TrySetResult(); }
        public void ShowError(string message, object owner)
        {
            Assert.Same(Owner, owner);
            calls.Add("error");
            Completed.TrySetResult();
        }
        public IReadOnlyList<UpdateBlocker> Find(string directory, IUpdateBlockerClassifier classifier, bool enrich = true)
        {
            Assert.Equal("unused", directory);
            Assert.Same(Classifier, classifier);
            calls.Add(enrich ? "find" : "find-final");
            if (ClosedBlocker != null) Completed.TrySetResult();
            return Blockers;
        }
        public Task<bool> CloseAsync(UpdateBlocker blocker, bool force)
        {
            Assert.False(force);
            ClosedBlocker = blocker;
            calls.Add("close");
            return Task.FromResult(false);
        }
    }

    private sealed class Backend(List<string> calls, bool failApply) : IUpdateBackend
    {
        public bool IsInstalled => true;
        public string CurrentVersion => "1.0.0";
        public string ContentDirectory => "unused";
        public Task<UpdateRelease?> CheckAsync() => Task.FromResult<UpdateRelease?>(new("2.0.0", "", new object()));
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token)
        {
            calls.Add("download");
            return Task.CompletedTask;
        }
        public void Apply(UpdateRelease release)
        {
            calls.Add("apply");
            if (failApply) throw new IOException("apply failed");
        }
    }
}
