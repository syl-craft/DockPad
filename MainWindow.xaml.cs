using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinContextMenuManager.Models;
using WinContextMenuManager.Services;

namespace WinContextMenuManager;

public partial class MainWindow : Window
{
    private List<ContextMenuEntryViewModel> _allEntries = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadEntries();
    }

    private void LoadEntries()
    {
        try
        {
            _allEntries = RegistryService.LoadAll()
                .Where(e => e.Target is ContextMenuTarget.Files
                                     or ContextMenuTarget.Folders
                                     or ContextMenuTarget.FolderBackground)
                .Where(e => !string.IsNullOrWhiteSpace(e.Command))
                .Select(e => new ContextMenuEntryViewModel(e))
                .ToList();

            ApplyFilter();
            TxtStatus.Text = $"{_allEntries.Count} raccourci(s) trouvé(s) au total.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Erreur de chargement : {ex.Message}";
            MessageBox.Show($"Impossible de lire le registre :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (LvEntries == null) return;

        IEnumerable<ContextMenuEntryViewModel> filtered = _allEntries;

        if (CmbFilter.SelectedItem is ComboBoxItem item && item.Tag?.ToString() != "All")
        {
            if (Enum.TryParse<ContextMenuTarget>(item.Tag?.ToString(), out var target))
                filtered = filtered.Where(e => e.Target == target);
        }

        var list = filtered.OrderBy(e => e.TargetLabel).ThenBy(e => e.DisplayName).ToList();
        LvEntries.ItemsSource = list;
        TxtEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Auto-resize columns to content after layout pass
        Dispatcher.InvokeAsync(AutoResizeColumns, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void AutoResizeColumns()
    {
        if (LvEntries.View is not GridView gridView) return;
        foreach (var column in gridView.Columns)
        {
            column.Width = column.ActualWidth;
            column.Width = double.NaN;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EntryDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var entry = dialog.Entry;

        if (RegistryService.KeyExists(entry.Target, entry.RegistryKey))
        {
            var r = MessageBox.Show(
                $"Une entrée '{entry.RegistryKey}' existe déjà pour cette cible.\nVoulez-vous la remplacer ?",
                "Conflit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        try
        {
            RegistryService.Save(entry);
            LoadEntries();
            TxtStatus.Text = $"Raccourci « {entry.DisplayName} » ajouté avec succès.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'écrire dans le registre :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void LvEntries_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void EditSelected()
    {
        if (LvEntries.SelectedItem is not ContextMenuEntryViewModel vm) return;

        var original = vm.ToModel();
        var dialog = new EntryDialog(original) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var updated = dialog.Entry;

        try
        {
            // If key or target changed, delete old entry first
            bool keyChanged = original.RegistryKey != updated.RegistryKey || original.Target != updated.Target;
            if (keyChanged)
                RegistryService.Delete(original);

            RegistryService.Save(updated);
            LoadEntries();
            TxtStatus.Text = $"Raccourci « {updated.DisplayName} » mis à jour.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la modification :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Presets_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PresetsDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            LoadEntries();
            TxtStatus.Text = $"{dialog.InstalledCount} raccourci(s) prédéfini(s) installé(s).";
        }
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (LvEntries.SelectedItem is not ContextMenuEntryViewModel vm) return;

        var copy = new ContextMenuEntry
        {
            DisplayName = vm.DisplayName + " (copie)",
            RegistryKey = vm.RegistryKey + "_copy",
            Command = vm.Command,
            IconPath = vm.IconPath,
            Target = vm.Target
        };

        var dialog = new EntryDialog(copy) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var entry = dialog.Entry;

        if (RegistryService.KeyExists(entry.Target, entry.RegistryKey))
        {
            var r = MessageBox.Show(
                $"Une entrée '{entry.RegistryKey}' existe déjà pour cette cible.\nVoulez-vous la remplacer ?",
                "Conflit", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        try
        {
            RegistryService.Save(entry);
            LoadEntries();
            TxtStatus.Text = $"Raccourci « {entry.DisplayName} » dupliqué avec succès.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'écrire dans le registre :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (LvEntries.SelectedItem is not ContextMenuEntryViewModel vm) return;

        var r = MessageBox.Show(
            $"Supprimer le raccourci « {vm.DisplayName} » ({vm.TargetLabel}) ?\n\nCette action est irréversible.",
            "Confirmer la suppression", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            RegistryService.Delete(vm.ToModel());
            LoadEntries();
            TxtStatus.Text = $"Raccourci « {vm.DisplayName} » supprimé.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la suppression :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadEntries();

    private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void LvEntries_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = LvEntries.SelectedItem != null;
        BtnEdit.IsEnabled = hasSelection;
        BtnDuplicate.IsEnabled = hasSelection;
        BtnDelete.IsEnabled = hasSelection;
        if (LvEntries.ContextMenu != null)
            LvEntries.ContextMenu.IsEnabled = hasSelection;
    }
}
