using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using WinContextMenuManager.Models;
using WinContextMenuManager.Services;

namespace WinContextMenuManager;

public partial class PresetsDialog : Window
{
    private List<PresetEntry> _presets = new();
    public int InstalledCount { get; private set; }

    public PresetsDialog()
    {
        InitializeComponent();

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
            var current = RegistryService.GetValues(preset.Target, preset.RegistryKey);

            if (current == null)
            {
                preset.Status = PresetStatus.NotInstalled;
                preset.IsSelected = true;
            }
            else if (current.Value.Command == preset.Command && current.Value.Icon == preset.IconPath)
            {
                preset.Status = PresetStatus.UpToDate;
                preset.IsSelected = false;
            }
            else
            {
                preset.Status = PresetStatus.UpdateAvailable;
                preset.IsSelected = true;
            }
        }

        ItemsPresets.ItemsSource = _presets;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var toInstall = _presets.Where(p => p.IsSelected && p.CanSelect).ToList();
        if (toInstall.Count == 0)
        {
            MessageBox.Show("Aucun raccourci sélectionné.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
                errors.Add($"{preset.DisplayName} : {ex.Message}");
            }
        }

        if (errors.Count > 0)
            MessageBox.Show($"Erreurs :\n{string.Join("\n", errors)}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Warning);

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
