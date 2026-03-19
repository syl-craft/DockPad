using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WinContextMenuManager.Models;
using WinContextMenuManager.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace WinContextMenuManager;

public partial class ShortcutDialog : Window
{
    public ShortcutEntry Entry { get; private set; }

    private readonly List<TerminalInfo> _terminals;

    public ShortcutDialog(ShortcutEntry? existing = null, int row = 0, int col = 0)
    {
        InitializeComponent();

        Entry = existing != null
            ? new ShortcutEntry
            {
                Row = existing.Row, Col = existing.Col,
                Name = existing.Name, Type = existing.Type,
                Command = existing.Command, IconPath = existing.IconPath,
                Terminal = existing.Terminal == null ? null : new TerminalConfig
                {
                    ExePath           = existing.Terminal.ExePath,
                    StartingDirectory = existing.Terminal.StartingDirectory,
                    RunCommand        = existing.Terminal.RunCommand,
                    NewTab            = existing.Terminal.NewTab,
                    ExtraArgs         = existing.Terminal.ExtraArgs,
                }
            }
            : new ShortcutEntry { Row = row, Col = col };

        TxtHeader.Text   = existing != null ? "Modifier le raccourci" : "Nouveau raccourci";
        TxtName.Text     = Entry.Name;
        TxtCommand.Text  = Entry.Command;
        TxtIconPath.Text = Entry.IconPath;

        _terminals = TerminalDetectionService.Detect();
        CmbTerminal.ItemsSource = _terminals;

        SetTypeCombo(Entry.Type);
        TxtIconPath.TextChanged += (_, _) => RefreshIconPreview();
        RefreshIconPreview();
    }

    // ── Type ─────────────────────────────────────────────────────────────────

    private void SetTypeCombo(ShortcutType type)
    {
        foreach (ComboBoxItem item in CmbType.Items)
        {
            if (item.Tag?.ToString() == type.ToString())
            {
                CmbType.SelectedItem = item;
                return;
            }
        }
        CmbType.SelectedIndex = 0;
    }

    private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelCommand == null) return; // pas encore initialisé

        var tag = (CmbType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        bool isTerminal = tag == "OpenTerminal";

        PanelCommand.Visibility  = isTerminal ? Visibility.Collapsed : Visibility.Visible;
        PanelTerminal.Visibility = isTerminal ? Visibility.Visible   : Visibility.Collapsed;

        if (!isTerminal)
        {
            LblCommand.Content = tag switch
            {
                "OpenFolder" or "OpenTerminal" => "Chemin du dossier *",
                "OpenUrl"                      => "URL *",
                _                              => "Commande *",
            };
            BtnBrowse.Visibility = tag == "OpenUrl" ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            // Pré-remplir depuis Entry.Terminal si disponible
            if (Entry.Terminal != null && CmbTerminal.SelectedItem == null)
                RestoreTerminalFields(Entry.Terminal);
            else if (CmbTerminal.SelectedItem == null && _terminals.Count > 0)
                CmbTerminal.SelectedIndex = 0;
        }
    }

    private void RestoreTerminalFields(TerminalConfig cfg)
    {
        // Chercher le terminal dans la liste
        var match = _terminals.FirstOrDefault(t =>
            string.Equals(t.ExePath, cfg.ExePath, StringComparison.OrdinalIgnoreCase));

        if (match == null && !string.IsNullOrEmpty(cfg.ExePath))
        {
            // Terminal custom non détecté — l'ajouter
            match = new TerminalInfo
            {
                DisplayName    = Path.GetFileNameWithoutExtension(cfg.ExePath),
                ExePath        = cfg.ExePath,
                SupportsNewTab = Path.GetFileNameWithoutExtension(cfg.ExePath)
                                     .Equals("wt", StringComparison.OrdinalIgnoreCase),
            };
            _terminals.Add(match);
            CmbTerminal.ItemsSource = null;
            CmbTerminal.ItemsSource = _terminals;
        }

        CmbTerminal.SelectedItem = match;
        TxtTerminalDir.Text         = cfg.StartingDirectory;
        TxtTerminalCommand.Text     = cfg.RunCommand;
        ChkNewTab.IsChecked         = cfg.NewTab;
        TxtTerminalExtraArgs.Text   = cfg.ExtraArgs;
    }

    // ── Terminal ──────────────────────────────────────────────────────────────

    private void CmbTerminal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChkNewTab == null) return;
        var supportsTab = (CmbTerminal.SelectedItem as TerminalInfo)?.SupportsNewTab ?? false;
        ChkNewTab.Visibility = supportsTab ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    private void BrowseTerminal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choisir un terminal",
            Filter = "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        // Vérifier si déjà dans la liste
        var existing = _terminals.FirstOrDefault(t =>
            string.Equals(t.ExePath, dlg.FileName, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            existing = new TerminalInfo
            {
                DisplayName    = Path.GetFileNameWithoutExtension(dlg.FileName),
                ExePath        = dlg.FileName,
                SupportsNewTab = Path.GetFileNameWithoutExtension(dlg.FileName)
                                     .Equals("wt", StringComparison.OrdinalIgnoreCase),
            };
            _terminals.Add(existing);
            CmbTerminal.ItemsSource = null;
            CmbTerminal.ItemsSource = _terminals;
        }

        CmbTerminal.SelectedItem = existing;
    }

    private void BrowseTerminalDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description        = "Choisir le dossier de départ",
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(TxtTerminalDir.Text))
            dlg.InitialDirectory = TxtTerminalDir.Text;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtTerminalDir.Text = dlg.SelectedPath;
    }

    private void TerminalField_Changed(object sender, EventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (TxtCmdPreview == null) return;
        var cfg = BuildTerminalConfig();
        if (cfg == null) { TxtCmdPreview.Text = ""; return; }

        var args = TerminalDetectionService.BuildArgs(cfg);
        TxtCmdPreview.Text = string.IsNullOrEmpty(args)
            ? $"\"{cfg.ExePath}\""
            : $"\"{cfg.ExePath}\" {args}";
    }

    private TerminalConfig? BuildTerminalConfig()
    {
        if (CmbTerminal.SelectedItem is not TerminalInfo terminal) return null;
        return new TerminalConfig
        {
            ExePath           = terminal.ExePath,
            StartingDirectory = TxtTerminalDir.Text.Trim(),
            RunCommand        = TxtTerminalCommand.Text.Trim(),
            NewTab            = ChkNewTab.IsChecked == true,
            ExtraArgs         = TxtTerminalExtraArgs.Text.Trim(),
        };
    }

    // ── Commande standard ────────────────────────────────────────────────────

    private void BrowseCommand_Click(object sender, RoutedEventArgs e)
    {
        var tag = (CmbType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (tag is "OpenFolder")
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description        = "Choisir un dossier",
                UseDescriptionForTitle = true,
            };
            if (Directory.Exists(TxtCommand.Text))
                dlg.InitialDirectory = TxtCommand.Text;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtCommand.Text = dlg.SelectedPath;
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Choisir un exécutable",
                Filter = "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtCommand.Text = dlg.FileName;
        }
    }

    // ── Icône ────────────────────────────────────────────────────────────────

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choisir une icône",
            Filter = "Images et exécutables|*.png;*.ico;*.bmp;*.jpg;*.jpeg;*.exe;*.dll|Tous les fichiers|*.*"
        };
        if (File.Exists(TxtIconPath.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(TxtIconPath.Text);
        if (dlg.ShowDialog() == true)
            TxtIconPath.Text = dlg.FileName;
    }

    private void RefreshIconPreview()
    {
        try
        {
            string path = TxtIconPath.Text.Trim().Split(',')[0].Trim('"');
            if (!File.Exists(path)) { ImgIconPreview.Source = null; return; }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".ico" or ".png" or ".bmp" or ".jpg" or ".jpeg")
            {
                ImgIconPreview.Source = new BitmapImage(new Uri(path));
                return;
            }
            if (ext is ".exe" or ".dll")
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) { ImgIconPreview.Source = null; return; }
                using var bmp = icon.ToBitmap();
                var handle = bmp.GetHbitmap();
                try
                {
                    ImgIconPreview.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        handle, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally { DeleteObject(handle); }
                return;
            }
        }
        catch { }
        ImgIconPreview.Source = null;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    // ── Enregistrement ───────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Le nom est obligatoire.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        ShortcutType type = ShortcutType.RunCommand;
        if (CmbType.SelectedItem is ComboBoxItem item)
            Enum.TryParse(item.Tag?.ToString(), out type);

        if (type == ShortcutType.OpenTerminal)
        {
            var cfg = BuildTerminalConfig();
            if (cfg == null)
            {
                MessageBox.Show("Veuillez sélectionner un terminal.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbTerminal.Focus();
                return;
            }
            Entry.Name     = name;
            Entry.Type     = type;
            Entry.Terminal = cfg;
            Entry.Command  = TxtCmdPreview.Text; // pour le tooltip
            Entry.IconPath = TxtIconPath.Text.Trim();
        }
        else
        {
            string command = TxtCommand.Text.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                MessageBox.Show("La commande / le chemin est obligatoire.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCommand.Focus();
                return;
            }
            Entry.Name     = name;
            Entry.Type     = type;
            Entry.Command  = command;
            Entry.Terminal = null;
            Entry.IconPath = TxtIconPath.Text.Trim();
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
