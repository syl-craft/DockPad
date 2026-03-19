using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DockPad.Models;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DockPad;

public partial class EntryDialog : Window
{
    public ContextMenuEntry Entry { get; private set; }
    private readonly bool _isEdit;
    private readonly string _originalKey;
    private readonly ContextMenuTarget _originalTarget;

    public EntryDialog(ContextMenuEntry? existing = null)
    {
        InitializeComponent();

        _isEdit = existing != null;
        Entry = existing != null
            ? new ContextMenuEntry
            {
                RegistryKey = existing.RegistryKey,
                DisplayName = existing.DisplayName,
                Command = existing.Command,
                IconPath = existing.IconPath,
                Target = existing.Target
            }
            : new ContextMenuEntry();

        _originalKey = Entry.RegistryKey;
        _originalTarget = Entry.Target;

        Title = _isEdit ? "Modifier le raccourci" : "Nouveau raccourci";

        TxtDisplayName.Text = Entry.DisplayName;
        TxtRegistryKey.Text = Entry.RegistryKey;
        TxtCommand.Text = Entry.Command;
        TxtIconPath.Text = Entry.IconPath;

        // Sync TxtRegistryKey auto from display name (only for new entries)
        if (!_isEdit)
            TxtDisplayName.TextChanged += (_, _) => AutoFillKey();

        SetTargetCombo(Entry.Target);
        TxtIconPath.TextChanged += (_, _) => RefreshIconPreview();
        RefreshIconPreview();
    }

    private void AutoFillKey()
    {
        if (_isEdit) return;
        string safe = System.Text.RegularExpressions.Regex.Replace(
            TxtDisplayName.Text.Trim(), @"[^a-zA-Z0-9_\-]", "_");
        TxtRegistryKey.Text = safe;
    }

    private void SetTargetCombo(ContextMenuTarget target)
    {
        foreach (ComboBoxItem item in CmbTarget.Items)
        {
            if (item.Tag?.ToString() == target.ToString())
            {
                CmbTarget.SelectedItem = item;
                return;
            }
        }
        CmbTarget.SelectedIndex = 0;
    }

    private void CmbTarget_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CmbTarget.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            PanelBgNote.Visibility = item.Tag?.ToString() == "FolderBackground"
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BrowseCommand_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir un exécutable",
            Filter = "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            TxtCommand.Text = $"\"{dlg.FileName}\" \"%1\"";
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir une icône",
            Filter = "Icônes et exécutables (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|Tous (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            TxtIconPath.Text = dlg.FileName;
    }

    private void RefreshIconPreview()
    {
        try
        {
            string path = TxtIconPath.Text.Trim().Split(',')[0].Trim('"');
            if (File.Exists(path) && path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                ImgIconPreview.Source = new BitmapImage(new Uri(path));
                return;
            }
        }
        catch { }
        ImgIconPreview.Source = null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string displayName = TxtDisplayName.Text.Trim();
        string regKey = TxtRegistryKey.Text.Trim();
        string command = TxtCommand.Text.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            AppDialog.Warning("Le nom affiché est obligatoire.", owner: this);
            TxtDisplayName.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(regKey))
        {
            AppDialog.Warning("La clé de registre est obligatoire.", owner: this);
            TxtRegistryKey.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(command))
        {
            AppDialog.Warning("La commande est obligatoire.", owner: this);
            TxtCommand.Focus();
            return;
        }

        ContextMenuTarget selectedTarget = ContextMenuTarget.Files;
        if (CmbTarget.SelectedItem is System.Windows.Controls.ComboBoxItem selected)
        {
            Enum.TryParse(selected.Tag?.ToString(), out selectedTarget);
        }

        Entry.DisplayName = displayName;
        Entry.RegistryKey = regKey;
        Entry.Command = command;
        Entry.IconPath = TxtIconPath.Text.Trim();
        Entry.Target = selectedTarget;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
