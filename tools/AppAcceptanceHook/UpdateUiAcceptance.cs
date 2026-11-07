using Mue.Wpf;
using Mue.Velopack;
using DockPad.Services.UpdateHost;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad;
using DockPad.Services;
using DockPad.Services.Localization;
using Mue;
using Velopack;

// In-process WPF integration tests: exercise framework OnClick (including IsCancel), never call
// private handlers or the update SDK instead of the production dialog's install path.
internal static class UpdateUiAcceptance
{
    private static string _root = "";
    private static string FixtureVersion(string key)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "versions.json")));
        return json.RootElement.GetProperty(key).GetString()!;
    }
    private static readonly List<string> Passed = [];
    public static async Task RunAsync(string root)
    {
        _root = root;
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
        var main = (QuickAccessWindow)Application.Current.MainWindow;
        _ = main.Dispatcher.BeginInvoke(new Action(() => main.Commands.OpenUpdates.Execute(null)));
        var entry = await Modal<UpdatesDialog>();
        Assert(entry.Owner == main && ReferenceEquals(entry.DataContext, main.Updates), "application command opens shared update dialog");
        entry.Close();
        await Until(() => !entry.IsVisible, "close menu dialog");
        var backend = new ControlledBackend();
        using var service = new UpdateService(backend, DockPadUpdates.ServiceOptions);
        var window = await Open(service);
        await Idle();
        Assert(service.Presentation.Primary != UpdateAction.Install, "install unavailable before discovery");
        var automatic = (CheckBox)window.FindName("Automatic");
        automatic.IsChecked = true; Click(automatic);
        Assert(AppSettingsService.Current.CheckForUpdates, "automatic checks enabled and saved");
        automatic.IsChecked = false; Click(automatic);
        Assert(!AppSettingsService.Current.CheckForUpdates, "automatic checks disabled and saved");

        backend.Mode = "none";
        Click(Button(window, "Update_Check"));
        await Until(() => service.State == UpdateState.Idle, "up to date");
        Assert(Texts(window).Contains(service.Status), "up-to-date message rendered");
        backend.Mode = "check-error";
        Click(Button(window, "Update_Check"));
        await Until(() => service.State == UpdateState.Failed, "offline check");
        Assert(Button(window, "Update_Retry").IsEnabled, "offline retry enabled");
        backend.Mode = "cancel";
        Click(Button(window, "Update_Retry"));
        await Until(() => service.State == UpdateState.Available, "available after retry");
        Assert(((StackPanel)window.FindName("NotesBlocks")).Children.Count > 0, "release notes rendered");
        Click((Button)window.FindName("InstallButton"));
        await Until(() => service.Progress == 42, "download progress");
        Assert(!((Button)window.FindName("InstallButton")).IsEnabled && service.Presentation.Secondary == UpdateAction.Stop, "download can only be stopped");
        var progress = Descendants<ProgressBar>(window).Single();
        Assert(progress.IsVisible && progress.Value == 42, "progress binding visible");
        Capture(window, "download-fr");
        Click(Button(window, "Update_Stop"));
        await Until(() => service.State == UpdateState.Available, "cancel download");
        Assert(window.IsVisible && backend.Applies == 0, "stop preserves window without applying");
        window.Close();

        window = await Open(service);
        backend.Mode = "download-error";
        Click((Button)window.FindName("InstallButton"));
        await Until(() => service.State == UpdateState.Failed, "download failure");
        Assert(Button(window, "Update_Retry").IsEnabled, "download retry enabled");
        backend.Mode = "apply-error";
        Click(Button(window, "Update_Retry"));
        await Until(() => service.State == UpdateState.Available, "available after download failure" );
        Click((Button)window.FindName("InstallButton"));
        var error = await Modal<AppDialog>();
        Assert(Texts(error).Any(t => t.Contains("fixture apply failure")), "apply error displayed");
        Click((Button)error.FindName("BtnPrimary"));
        await Idle();
        Assert(service.State == UpdateState.Ready && !App.IsExiting, "apply failure leaves app usable");
        Assert(!File.Exists(Path.Combine(root, "app", ".dockpad-update")), "failed handoff clears marker");

        // Another open editor must prevent installation, even after download is ready.
        var editor = new Window { Title = "Fixture unsaved editor", Width = 200, Height = 100 };
        editor.Show();
        var applies = backend.Applies;
        Click((Button)window.FindName("InstallButton"));
        error = await Modal<AppDialog>();
        Assert(Texts(error).Contains(Loc.T("Update_CloseDialogs")), "open editor warning");
        Click((Button)error.FindName("BtnPrimary"));
        editor.Close();
        await Idle();
        Assert(backend.Applies == applies, "open editor prevents apply");

        foreach (var language in new[] { "fr", "en", "qps-Ploc" })
        foreach (var dark in new[] { false, true })
        {
            Loc.SetCulture(CultureInfo.GetCultureInfo(language)); ThemeService.Apply(dark);
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await Idle();
            foreach (var button in Descendants<Button>(window).Where(b => b.IsVisible))
            {
                var rect = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                Assert(rect.Bottom <= window.ActualHeight && rect.Right <= window.ActualWidth, "buttons fit " + language + "/" + dark);
            }
            Capture(window, "minimum-" + language + (dark ? "-dark" : "-light"));
        }
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr")); ThemeService.Apply(false);
        window.Close();
        using (var unavailable = new UpdateService(new ControlledBackend { IsInstalled = false }, DockPadUpdates.ServiceOptions))
        {
            var legacy = await Open(unavailable); await Idle();
            Assert(unavailable.Presentation.Primary != UpdateAction.Install && Button(legacy, "Update_DownloadLatest").IsVisible, "legacy installation offers download");
            legacy.Close();
        }

        // The successful path uses actual Velopack packages, through the same buttons.
        var realService = new UpdateService(new VelopackBackend(new UpdateManager(Path.Combine(root, "feed")), DockPadUpdates.BackendOptions), DockPadUpdates.ServiceOptions);
        window = await Open(realService);
        Click(Button(window, "Update_Check"));
        await Until(() => realService.State == UpdateState.Available, "real package discovered");
        Assert(realService.Release!.Version == FixtureVersion("Next"), "real next version displayed");
        using var blocker = StartMcp();
        await Until(() => new WindowsUpdateBlockerFinder().Find(UpdateBlockerQuery.ForDirectory(AppContext.BaseDirectory), new DockPadBlockerClassifier()).Blockers.Any(b => b.Pid == blocker.Id), "real MCP image lock");
        Click((Button)window.FindName("InstallButton"));
        var blocked = await Modal<UpdateBlockersDialog>();
        await Idle();
        var list = (ItemsControl)blocked.FindName("Processes");
        var group = list.Items.Cast<UpdateBlockerGroup>().Where(g => g.Processes.Where(b => b.Pid == blocker.Id).Any()).Single();
        Assert(group.IsRelay && group.Selected == true, "MCP relay group selected by default");
        group.Selected = false;
        Assert(!((Button)blocked.FindName("CloseButton")).IsEnabled, "Refuse cannot ignore an unchecked group");
        Assert(((TextBlock)blocked.FindName("PreparationSummary")).Visibility == Visibility.Collapsed, "DockPad has no preparation summary");
        blocked.Width = blocked.MinWidth; blocked.Height = blocked.MinHeight;
        await Idle();
        Capture(blocked, "blockers-fr");
        Click(Button(blocked, "Update_Postpone"));
        await Until(() => !blocked.IsVisible, "postpone");
        Assert(!blocker.HasExited && realService.State == UpdateState.Ready, "postpone preserves process and download");

        Click((Button)window.FindName("InstallButton"));
        blocked = await Modal<UpdateBlockersDialog>();
        await Idle();
        Click((Button)blocked.FindName("CloseButton"));
        await Until(() => ((Border)blocked.FindName("ForceWarning")).IsVisible && ((Button)blocked.FindName("CloseButton")).IsEnabled, "soft close leaves explicit force choice");
        var remaining = (ItemsControl)blocked.FindName("RemainingProcesses");
        Assert(remaining.Items.Cast<UpdateBlocker>().Where(b => b.Pid == blocker.Id).Any(), "remaining list names the selected PID");
        Capture(blocked, "force-confirm-fr");
        Click(Button(blocked, "Update_Postpone"));
        await Until(() => !blocked.IsVisible, "decline force");
        Assert(!blocker.HasExited, "declining force preserves process");

        Click((Button)window.FindName("InstallButton"));
        blocked = await Modal<UpdateBlockersDialog>();
        Click((Button)blocked.FindName("CloseButton"));
        await Until(() => ((Border)blocked.FindName("ForceWarning")).IsVisible && ((Button)blocked.FindName("CloseButton")).IsEnabled, "force offered again");
        using var second = StartMcp();
        await Until(() => new WindowsUpdateBlockerFinder().Find(UpdateBlockerQuery.ForDirectory(AppContext.BaseDirectory), new DockPadBlockerClassifier()).Blockers.Where(b => b.Pid == second.Id).Any(), "second lock");
        Click((Button)blocked.FindName("CloseButton"));
        await Until(() => blocker.HasExited && ((Button)blocked.FindName("RetryButton")).IsEnabled, "approved force close");
        Assert(!second.HasExited && blocked.IsVisible, "only selected process terminated");
        second.StandardInput.Close();
        await Until(() => second.HasExited, "external lock released normally");
        Record("ready for real UI apply/restart");
        var hashes = new[] { "settings.json", "browsers.json", "mcp.json" }.ToDictionary(name => name,
            name => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "profile", name)))));
        File.WriteAllText(Path.Combine(root, "ui-profile.json"), System.Text.Json.JsonSerializer.Serialize(hashes));
        File.WriteAllText(Path.Combine(root, "ui-passed.json"), System.Text.Json.JsonSerializer.Serialize(Passed));
        Click((Button)blocked.FindName("RetryButton"));
        // Production Install_Click now applies, exits and restarts; outer driver verifies N+1.
    }

    public static async Task VerifyRestartAsync(string root)
    {
        _root = root;
        using var service = new UpdateService(new VelopackBackend(new UpdateManager(Path.Combine(root, "feed")), DockPadUpdates.BackendOptions), DockPadUpdates.ServiceOptions);
        var window = await Open(service);
        Click(Button(window, "Update_Check"));
        await Until(() => service.State == UpdateState.Idle && service.Status == Loc.T("Update_UpToDate"), "updated version reports up to date");
        Assert(service.CurrentVersion == FixtureVersion("Next") && service.Presentation.Primary != UpdateAction.Install, "restart UI shows new version without reinstall");
        Assert(((CheckBox)window.FindName("Automatic")).IsChecked == false, "automatic preference survives restart");
        window.Close();
    }

    private static Process StartMcp()
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "DockPad.exe"), "--mcp")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true };
        start.Environment.Remove("DOTNET_STARTUP_HOOKS");
        return Process.Start(start)!;
    }
    private static async Task<UpdatesDialog> Open(UpdateService service)
    {
        var window = new UpdatesDialog(service, DockPadUpdates.DialogHost) { Owner = Application.Current.MainWindow };
        _ = window.Dispatcher.BeginInvoke(new Action(() => window.ShowDialog()));
        await Until(() => window.IsVisible && window.IsLoaded, "update dialog opened modally");
        return window;
    }
    private static Button Button(Window window, string key) => Descendants<Button>(window).Single(b => Equals(b.Content, Loc.T(key)));
    private static void Click(ButtonBase button)
    {
        if (!button.IsEnabled || !button.IsVisible) throw new Exception("Cannot click disabled/hidden button: " + button.Content);
        void Raise()
        {
            try
            {
                if (button is CheckBox) button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                else typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(button, null);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(_root, "error.txt"), ex.ToString()); }
        }
        if (button is CheckBox) Raise();
        else button.Dispatcher.BeginInvoke(new Action(Raise));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static IEnumerable<string> Texts(Window window) => Descendants<TextBlock>(window).Select(t => t.Text);
    private static async Task<T> Modal<T>() where T : Window
    {
        await Until(() => Application.Current.Windows.OfType<T>().Any(w => w.IsVisible), typeof(T).Name);
        return Application.Current.Windows.OfType<T>().Single(w => w.IsVisible);
    }
    private static async Task Idle() { await Task.Delay(80); Application.Current.MainWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle); }
    private static async Task Until(Func<bool> predicate, string label)
    {
        for (int i = 0; i < 600; i++) { await Task.Delay(50); if (predicate()) { await Idle(); Record(label); return; } }
        throw new TimeoutException(label);
    }
    private static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); Record(label); }
    private static void Record(string label) { Passed.Add(label); File.AppendAllText(Path.Combine(_root, "ui-progress.txt"), label + Environment.NewLine); }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(_root, name + ".png")); encoder.Save(file);
    }
    private sealed class ControlledBackend : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;
        public string CurrentVersion => FixtureVersion("Base");
        public string ContentDirectory => AppContext.BaseDirectory;
        public string Mode = "none";
        public string Notes => "Version de test — téléchargement, annulation et reprise.\n" + new string('x', 180);
        public int Applies;
        public Task<UpdateRelease?> CheckAsync(CancellationToken token) => Mode == "check-error" ? throw new IOException("fixture offline") : Task.FromResult(Mode == "none" ? null : new UpdateRelease(FixtureVersion("Next"), Notes, new object()));
        public async Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token)
        {
            if (Mode == "download-error") throw new IOException("fixture download failure");
            progress(42);
            if (Mode == "cancel") await Task.Delay(Timeout.Infinite, token);
            progress(100);
        }
        public void Apply(UpdateRelease release) { Applies++; throw new IOException("fixture apply failure"); }
    }
}
