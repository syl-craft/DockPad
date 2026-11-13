using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DockPad.Services;
using Mue;
using Mue.Wpf;

namespace DockPad.Tests;

public class UpdateContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UpdateStylesAreCompleteInBothThemes(bool dark) => WpfTestHost.Run(() =>
    {
        try
        {
            ThemeService.Apply(dark);
            Assert.Empty(MueStyleKeys.Missing(Application.Current.TryFindResource));
            Assert.Equal(MueStyleKeys.Required, MueStyleKeys.Missing(_ => null));
            Assert.Equal(new[] { "Brush.Accent" }, MueStyleKeys.Missing(key => key.Equals("Brush.Accent") ? null : new object()));
        }
        finally { ThemeService.Apply(false); }
    });

    [Fact]
    public Task UpdateDefaultsOnlyFillMissingApplicationResources() => WpfTestHost.Run(() =>
    {
        var app = Application.Current;
        var resources = app.Resources;
        try
        {
            var accent = new SolidColorBrush(Colors.Purple);
            app.Resources = new ResourceDictionary { ["Brush.Accent"] = accent };
            MueStyles.AddMissingDefaults(app);
            Assert.Same(accent, app.FindResource("Brush.Accent"));
            Assert.Empty(MueStyleKeys.Missing(app.TryFindResource));
            var count = app.Resources.Count;
            MueStyles.AddMissingDefaults(app);
            Assert.Equal(count, app.Resources.Count);
        }
        finally { app.Resources = resources; }
    });

    [Theory]
    [InlineData(UpdateShutdownBehavior.Refuse, false, "Update_BlockedHelp")]
    [InlineData(UpdateShutdownBehavior.Refuse, true, "Update_IncompleteHelp")]
    [InlineData(UpdateShutdownBehavior.Kill, false, "Update_BlockedHelpForced")]
    [InlineData(UpdateShutdownBehavior.Kill, true, "Update_IncompleteHelpForced")]
    [InlineData(UpdateShutdownBehavior.AskToQuit, false, "Update_BlockedHelpAskToQuit")]
    [InlineData(UpdateShutdownBehavior.AskToQuit, true, "Update_IncompleteHelpAskToQuit")]
    public Task UpdateShutdownTextsFollowAllThreePolicies(UpdateShutdownBehavior behavior, bool incomplete, string key) => WpfTestHost.Run(() =>
    {
        var window = new UpdateBlockersDialog(UpdateBlockerQuery.ForDirectory("unused"), new([new() { Selected = true }]),
            new Host(), behavior, incomplete: incomplete);
        try { Assert.Equal(UpdateTexts.T(key), ((TextBlock)window.FindName("HelpText")).Text); }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(null)]
    [InlineData("The active session will close.")]
    public Task UpdatePreparationRunsOnceAfterTheFinalScan(string? summary) => WpfTestHost.Run(() =>
    {
        var host = new Host { Summary = summary };
        using var service = new UpdateService(new Backend(host.Calls));
        WpfTestHost.PumpUntilCompleted(service.CheckAsync());
        var window = new UpdatesDialog(service, host, dialog =>
        {
            Assert.NotNull(summary);
            Assert.True(((Button)dialog.FindName("CloseButton")).IsEnabled);
            Assert.Equal(summary, ((TextBlock)dialog.FindName("PreparationSummary")).Text);
            dialog.Close();
            return true;
        });
        try
        {
            PressAndWait(window);
            Assert.Null(host.Error);
            Assert.Equal(new[] { "download", "check", "find", "final", "prepare", "before", "apply", "exit" }, host.Calls);
            Assert.Equal(UpdateState.Installing, service.State);
            var text = (TextBlock)window.FindName("PreparationSummary");
            Assert.Equal(summary ?? "", text.Text);
            Assert.Equal(summary == null ? Visibility.Collapsed : Visibility.Visible, text.Visibility);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public Task UpdatePostponeOrCloseNeverPreparesTheHost(bool? answer) => WpfTestHost.Run(() =>
    {
        var host = new Host { Summary = "The active session will close.", Blockers = [new() { Pid = 42 }] };
        using var service = new UpdateService(new Backend(host.Calls));
        WpfTestHost.PumpUntilCompleted(service.CheckAsync());
        var window = new UpdatesDialog(service, host, dialog =>
        {
            host.Calls.Add("blockers");
            Assert.Equal(host.Summary, ((TextBlock)dialog.FindName("PreparationSummary")).Text);
            Assert.Equal(Visibility.Visible, ((TextBlock)dialog.FindName("PreparationSummary")).Visibility);
            dialog.Close();
            return answer;
        });
        try
        {
            PressAndWait(window);
            Assert.Null(host.Error);
            Assert.Equal(new[] { "download", "check", "find", "blockers" }, host.Calls);
            Assert.Equal(UpdateState.Ready, service.State);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task UpdateSummaryIsCollapsedWhenAbsentAndClearedAtTheNextSearch() => WpfTestHost.Run(() =>
    {
        var host = new Host { CanContinue = false, Summary = "Cannot close this session." };
        using var service = new UpdateService(new Backend(host.Calls));
        WpfTestHost.PumpUntilCompleted(service.CheckAsync());
        var window = new UpdatesDialog(service, host);
        var blockers = new UpdateBlockersDialog(UpdateBlockerQuery.ForDirectory("unused"), new([]), host, UpdateShutdownBehavior.Refuse);
        try
        {
            Assert.Equal(Visibility.Collapsed, ((TextBlock)blockers.FindName("PreparationSummary")).Visibility);
            PressAndWait(window);
            Assert.Null(host.Error);
            Assert.Equal(new[] { "download", "check" }, host.Calls);
            Assert.Equal(UpdateState.Ready, service.State);
            var text = (TextBlock)window.FindName("PreparationSummary");
            Assert.Equal(host.Summary, text.Text);
            typeof(UpdateService).GetProperty(nameof(UpdateService.State))?.SetValue(service, UpdateState.Idle);
            WpfTestHost.PumpUntilCompleted(service.CheckAsync());
            Assert.Equal(Visibility.Collapsed, text.Visibility);
            Assert.Equal("", text.Text);
        }
        finally { blockers.Close(); window.Close(); }
    });

    [Fact]
    public Task UpdateBadgeHasAFullClickableAreaAndAPill() => WpfTestHost.Run(() =>
    {
        var badge = new UpdateBadge { VersionIndicator = "1.0.0 → 2.0.0", Status = "Update available", HasRelease = true };
        var window = new Window { Content = badge };
        badge.ApplyTemplate();
        badge.Measure(new Size(300, 40));
        badge.Arrange(new Rect(0, 0, badge.DesiredSize.Width, 40));
        badge.UpdateLayout();
        Assert.Equal(40, badge.ActualHeight);
        Assert.NotNull(badge.Template);
        Assert.Equal(typeof(UpdateBadge), badge.Template.TargetType);
        Assert.Equal(26, Assert.IsType<Border>(badge.Template.FindName("Badge", badge)).ActualHeight);
        Assert.NotNull(VisualTreeHelper.HitTest(badge, new Point(badge.ActualWidth / 2, 1)));
        Assert.NotNull(VisualTreeHelper.HitTest(badge, new Point(badge.ActualWidth / 2, 39)));
        var clicks = 0;
        var commands = 0;
        badge.Click += (_, _) => clicks++;
        badge.Command = new BadgeCommand(() => commands++);
        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(badge, null);
        Assert.Equal(1, clicks);
        Assert.Equal(1, commands);
        window.Close();
    });

    private static void PressAndWait(UpdatesDialog window)
    {
        var previous = SynchronizationContext.Current ?? throw new InvalidOperationException("WPF context is missing");
        var operation = new InstallOperationContext(previous);
        SynchronizationContext.SetSynchronizationContext(operation);
        try { ((Button)window.FindName("InstallButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            operation.OperationCompleted();
        }
        WpfTestHost.PumpUntilCompleted(operation.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20)));
    }

    /// <summary>
    /// Le clic est async void : ses rappels précèdent le finally qui rend la main à l'utilisateur.
    /// Le contexte compte les opérations jusqu'à leur vraie fin, y compris les clics async void imbriqués de Mue.
    /// </summary>
    private sealed class InstallOperationContext(SynchronizationContext dispatcher) : SynchronizationContext
    {
        // Cette première opération garde le contexte ouvert jusqu'au retour synchrone de RaiseEvent.
        private int _operations = 1;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void OperationStarted() => Interlocked.Increment(ref _operations);
        public override void OperationCompleted()
        {
            if (Interlocked.Decrement(ref _operations) == 0) Completed.TrySetResult();
        }
        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state) => dispatcher.Post(value => Invoke(callback, value), state);
        public override void Send(SendOrPostCallback callback, object? state) => dispatcher.Send(value => Invoke(callback, value), state);
        private void Invoke(SendOrPostCallback callback, object? state)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            finally { SetSynchronizationContext(previous); }
        }
    }

    private sealed class BadgeCommand(Action execute) : System.Windows.Input.ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private sealed class Host : IUpdateDialogHost, IUpdateBlockerFinder
    {
        public List<string> Calls { get; } = [];
        public string? Error { get; private set; }
        public bool CanContinue { get; init; } = true;
        public string? Summary { get; init; }
        public IReadOnlyList<UpdateBlocker> Blockers { get; init; } = [];
        public IUpdateLog Log { get; } = new SilentUpdateLog();
        public IUpdateBlockerClassifier Classifier { get; } = new ProgramClassifier();
        public IUpdateBlockerFinder Finder => this;
        public bool IsExiting => false;
        public string LatestReleasePage => "unused";
        public string ReleasePage(string version) => "unused";
        public Task<UpdatePreparationCheck> CheckAsync(object owner, CancellationToken token)
        {
            Calls.Add("check");
            return Task.FromResult(new UpdatePreparationCheck(CanContinue, Summary));
        }
        public Task<bool> PrepareAsync(object owner, CancellationToken token) { Calls.Add("prepare"); return Task.FromResult(true); }
        public void BeforeApply() => Calls.Add("before");
        public void ApplyFailed() => Calls.Add("failed");
        public void ExitAfterApply() => Calls.Add("exit");
        public void ShowError(string message, object owner) => Error = message;
        public UpdateBlockerScan Find(UpdateBlockerQuery query, IUpdateBlockerClassifier classifier, bool enrich = true)
        { Calls.Add(enrich ? "find" : "final"); return new(Blockers); }
        public Task<UpdateBlockerCloseResult> CloseAsync(UpdateBlocker blocker, bool force, CancellationToken token = default) =>
            Task.FromResult(UpdateBlockerCloseResult.StillRunning);
    }

    private sealed class Backend(List<string> calls) : IUpdateBackend
    {
        public bool IsInstalled => true;
        public string CurrentVersion => "1.0.0";
        public string ContentDirectory => "unused";
        public Task<UpdateRelease?> CheckAsync(CancellationToken token) => Task.FromResult<UpdateRelease?>(new("2.0.0", "", new object()));
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token) { calls.Add("download"); return Task.CompletedTask; }
        public void Apply(UpdateRelease release) => calls.Add("apply");
    }
}
