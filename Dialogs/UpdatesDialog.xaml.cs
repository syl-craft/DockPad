using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DockPad.Services;
using DockPad.Services.Updates;

namespace DockPad;

public partial class UpdatesDialog : Window
{
    private readonly UpdateService _updates;
    private bool _preparing;
    public UpdatesDialog() : this(UpdateService.Current) { }
    public UpdatesDialog(UpdateService updates)
    {
        InitializeComponent(); _updates = updates; DataContext = updates;
        Automatic.IsChecked = AppSettingsService.Current.CheckForUpdates;
    }
    private void Automatic_Click(object sender, RoutedEventArgs e) =>
        AppSettingsService.Update(s => s.CheckForUpdates = Automatic.IsChecked == true);
    private async void Check_Click(object sender, RoutedEventArgs e) => await _updates.CheckAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) { _updates.CancelDownload(); Close(); }
    private void DownloadLatest_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/syl-craft/DockPad/releases/latest") { UseShellExecute = true });
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_preparing && !App.IsExiting) e.Cancel = true;
        if (!e.Cancel) _updates.CancelDownload();
        base.OnClosing(e);
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_preparing || !await _updates.DownloadAsync()) return;
        _preparing = true; InstallButton.IsEnabled = false;
        try
        {
            if (AppInstallation.IsWithin(AppPaths.ProfileRoot, AppInstallation.Root ?? _updates.ContentDirectory))
                throw new IOException(Loc.T("Update_ProfileInside"));
            if (Application.Current is not App app || !await app.PrepareForUpdateAsync(this)) return;
            var blockers = await Task.Run(() => UpdateBlockers.Find(_updates.ContentDirectory));
            if (blockers.Count > 0 && new UpdateBlockersDialog(_updates.ContentDirectory, blockers) { Owner = this }.ShowDialog() != true) return;
            // A final check improves feedback. The native updater independently refuses any race.
            if ((await Task.Run(() => UpdateBlockers.Find(_updates.ContentDirectory))).Count > 0)
                throw new IOException(Loc.T("Update_LockError"));
            if (!await app.PrepareForUpdateAsync(this)) return;
            AppInstallation.BeginUpdate();
            try { app.BeginUpdateHandoff(); _updates.Apply(); }
            catch { AppInstallation.EndUpdate(); app.CancelUpdateHandoff(); throw; }
            // Drain accepted relay requests: after the marker they go to the stable launcher.
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(App.Exit));
        }
        catch (Exception ex) { LogService.Warn(ex, "Cannot apply update"); AppDialog.Error(ex.Message, Loc.T("Update_Title"), this); }
        finally { _preparing = false; InstallButton.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(UpdateService.CanInstall))); }
    }
}
