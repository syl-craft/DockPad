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
using DockPad.Services.Updates;
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
        Click(Button(entry, "Common_Cancel"));
        await Until(() => !entry.IsVisible, "close menu dialog");
        var backend = new ControlledBackend();
        using var service = new UpdateService(backend);
        var window = await Open(service);
        await Idle();
        Assert(!Button(window, "Update_Install").IsEnabled, "install disabled before discovery");
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
        Assert(Button(window, "Update_Check").IsEnabled, "offline retry enabled");
        backend.Mode = "cancel";
        Click(Button(window, "Update_Check"));
        await Until(() => service.State == UpdateState.Available, "available after retry");
        Assert(Texts(window).Contains(backend.Notes), "release notes rendered");
        Click(Button(window, "Update_Install"));
        await Until(() => service.Progress == 42, "download progress");
        Assert(!Button(window, "Update_Check").IsEnabled && !Button(window, "Update_Install").IsEnabled, "busy buttons disabled");
        var progress = Descendants<ProgressBar>(window).Single();
        Assert(progress.IsVisible && progress.Value == 42, "progress binding visible");
        Capture(window, "download-fr");
        Click(Button(window, "Common_Cancel"));
        await Until(() => service.State == UpdateState.Available, "cancel download");
        Assert(!window.IsVisible && backend.Applies == 0, "cancel closes without applying");

        window = await Open(service);
        backend.Mode = "download-error";
        Click(Button(window, "Update_Install"));
        await Until(() => service.State == UpdateState.Failed, "download failure");
        Assert(Button(window, "Update_Install").IsEnabled, "download retry enabled");
        backend.Mode = "apply-error";
        Click(Button(window, "Update_Install"));
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
        Click(Button(window, "Update_Install"));
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
        using (var unavailable = new UpdateService(new ControlledBackend { IsInstalled = false }))
        {
            var legacy = await Open(unavailable); await Idle();
            Assert(!Button(legacy, "Update_Check").IsEnabled && Button(legacy, "Update_DownloadLatest").IsVisible, "legacy installation offers download");
            legacy.Close();
        }

        // The successful path uses actual Velopack packages, through the same buttons.
        var realService = new UpdateService(new VelopackBackend(new UpdateManager(Path.Combine(root, "feed"))));
        window = await Open(realService);
        Click(Button(window, "Update_Check"));
        await Until(() => realService.State == UpdateState.Available, "real package discovered");
        Assert(realService.Release!.Version == FixtureVersion("Next"), "real next version displayed");
        using var blocker = StartMcp();
        await Until(() => UpdateBlockers.Find(AppContext.BaseDirectory).Any(b => b.Pid == blocker.Id), "real MCP image lock");
        Click(Button(window, "Update_Install"));
        var blocked = await Modal<UpdateBlockersDialog>();
        await Idle();
        var list = (ListBox)blocked.FindName("Processes");
        Assert(list.Items.Cast<UpdateBlocker>().Any(b => b.Pid == blocker.Id), "blocking PID listed");
        Assert(!list.Items.Cast<UpdateBlocker>().Any(b => b.Selected), "no process selected by default");
        blocked.Width = blocked.MinWidth; blocked.Height = blocked.MinHeight;
        await Idle();
        var pathText = Descendants<TextBlock>(list).Single(t => t.Text == list.Items.Cast<UpdateBlocker>().Single(b => b.Pid == blocker.Id).File);
        Assert(pathText.ActualHeight > 20 && pathText.ActualWidth < list.ActualWidth, "long process path wraps at minimum width");
        Click((Button)blocked.FindName("CloseButton"));
        await Idle();
        Assert(!blocker.HasExited, "empty selection does not kill");
        Capture(blocked, "blockers-fr");
        Click(Button(blocked, "Update_Postpone"));
        await Until(() => !blocked.IsVisible, "postpone");
        Assert(!blocker.HasExited && realService.State == UpdateState.Ready, "postpone preserves process and download");

        Click(Button(window, "Update_Install"));
        blocked = await Modal<UpdateBlockersDialog>();
        await Idle();
        list = (ListBox)blocked.FindName("Processes");
        var check = Descendants<CheckBox>(list).Single(c => c.DataContext is UpdateBlocker b && b.Pid == blocker.Id);
        check.IsChecked = true; Click(check);
        Assert(list.Items.Cast<UpdateBlocker>().Single(b => b.Pid == blocker.Id).Selected, "process selection binding");
        Click((Button)blocked.FindName("CloseButton"));
        var confirm = await Modal<AppDialog>();
        Assert(Texts(confirm).Any(t => t.Contains(blocker.Id.ToString())), "force prompt names PID");
        Capture(confirm, "force-confirm-fr");
        Click((Button)confirm.FindName("BtnSecondary"));
        await Until(() => ((Button)blocked.FindName("CloseButton")).IsEnabled, "decline force");
        Assert(!blocker.HasExited && blocked.IsVisible, "declining force preserves process");
        // Rescanning rebuilds the list: selection must be explicit again.
        list = (ListBox)blocked.FindName("Processes");
        await Idle();
        check = Descendants<CheckBox>(list).Single(c => c.DataContext is UpdateBlocker b && b.Pid == blocker.Id);
        check.IsChecked = true; Click(check);
        Click((Button)blocked.FindName("CloseButton"));
        confirm = await Modal<AppDialog>();
        // Stop before apply to test Retry with a second lock after an explicitly approved kill.
        using var second = StartMcp();
        await Until(() => UpdateBlockers.Find(AppContext.BaseDirectory).Any(b => b.Pid == second.Id), "second lock");
        Click((Button)confirm.FindName("BtnPrimary"));
        await Until(() => blocker.HasExited && ((Button)blocked.FindName("RetryButton")).IsEnabled, "approved close");
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
        using var service = new UpdateService(new VelopackBackend(new UpdateManager(Path.Combine(root, "feed"))));
        var window = await Open(service);
        Click(Button(window, "Update_Check"));
        await Until(() => service.State == UpdateState.Idle && service.Status == Loc.T("Update_UpToDate"), "updated version reports up to date");
        Assert(service.CurrentVersion == FixtureVersion("Next") && !Button(window, "Update_Install").IsEnabled, "restart UI shows new version without reinstall");
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
        var window = new UpdatesDialog(service) { Owner = Application.Current.MainWindow };
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
        public Task<UpdateRelease?> CheckAsync() => Mode == "check-error" ? throw new IOException("fixture offline") : Task.FromResult(Mode == "none" ? null : new UpdateRelease(FixtureVersion("Next"), Notes, new object()));
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
