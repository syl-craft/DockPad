using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using DockPad.Models;
using DockPad.Services;

namespace DockPad;

public partial class PresetsDialog : Window
{
    private List<PresetEntry> _presets = new();
    public int InstalledCount { get; private set; }

    public PresetsDialog()
    {
        InitializeComponent();
        TxtVersion.Text = Services.AppInfo.VersionText;

        using var identity = WindowsIdentity.GetCurrent();
        bool isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (!isAdmin)
        {
            BtnInstall.Visibility = Visibility.Collapsed;
            BtnElevate.Visibility = Visibility.Visible;
        }

        LoadPresets();
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas"
            });
            App.Exit();
        }
        catch { /* UAC refusé par l'utilisateur */ }
    }

    private void LoadPresets()
    {
        _presets = PresetService.GetPresets();

        foreach (var preset in _presets)
        {
            preset.Status = PresetService.CompareStatus(
                RegistryService.GetValues(preset.Target, preset.RegistryKey), preset);
            preset.IsSelected = preset.Status != PresetStatus.UpToDate;
        }

        ItemsPresets.ItemsSource = _presets;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var toInstall = _presets.Where(p => p.IsSelected && p.CanSelect).ToList();
        if (toInstall.Count == 0)
        {
            AppDialog.Info(Loc.T("Presets_NoneSelected"), owner: this);
            return;
        }

        var errors = new List<string>();
        foreach (var preset in toInstall)
        {
            try
            {
                RegistryService.Save(new ContextMenuEntry
                {
                    DisplayName = preset.DisplayName,
                    RegistryKey = preset.RegistryKey,
                    Command = preset.Command,
                    IconPath = preset.IconPath,
                    Target = preset.Target
                });
                InstalledCount++;
            }
            catch (Exception ex)
            {
                Services.LogService.Error(ex, $"Installation du prédéfini « {preset.DisplayName} »");
                errors.Add($"{preset.DisplayName} : {ex.Message}");
            }
        }

        if (errors.Count > 0)
            AppDialog.Error($"Erreurs :\n{string.Join("\n", errors)}", owner: this);

        DialogResult = InstalledCount > 0;
        Close();
    }

    private void OpenContextMenuManager_Click(object sender, RoutedEventArgs e)
    {
        var win = new ContextMenuManagerWindow();
        win.Show();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadPresets();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
