using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DockPad;

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
                Command = existing.Command,
                IconPath        = existing.IconPath,
                IconProfilePath = existing.IconProfilePath,
                Terminal = existing.Terminal == null ? null : new TerminalConfig
                {
                    ExePath           = existing.Terminal.ExePath,
                    StartingDirectory = existing.Terminal.StartingDirectory,
                    RunCommand        = existing.Terminal.RunCommand,
                    NewTab            = existing.Terminal.NewTab,
                    ExtraArgs         = existing.Terminal.ExtraArgs,
                },
                ProcessSwitch = existing.ProcessSwitch == null ? null : new ProcessSwitchConfig
                {
                    ProcessName = existing.ProcessSwitch.ProcessName,
                    Executable  = existing.ProcessSwitch.Executable,
                    Parameters  = existing.ProcessSwitch.Parameters,
                },
            }
            : new ShortcutEntry { Row = row, Col = col };

        // Si le fichier source n'existe plus, effacer iconPath et afficher le chemin profil
        if (!string.IsNullOrEmpty(Entry.IconPath) && !File.Exists(Entry.IconPath))
            Entry.IconPath = "";

        TxtHeader.Text   = Loc.T(existing != null ? "Shortcut_Header_Edit" : "Shortcut_Header_New");
        TxtName.Text     = Entry.Name;
        TxtCommand.Text  = Entry.Command;
        TxtIconPath.Text = !string.IsNullOrEmpty(Entry.IconPath)
            ? Entry.IconPath
            : IconStoreService.ResolveProfilePath(Entry.IconProfilePath) ?? "";

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
        bool isTerminal       = tag == "OpenTerminal";
        bool isProcessSwitch  = tag == "SwitchToProcess";
        bool isStandard       = !isTerminal && !isProcessSwitch;

        PanelCommand.Visibility       = isStandard       ? Visibility.Visible    : Visibility.Collapsed;
        PanelTerminal.Visibility      = isTerminal        ? Visibility.Visible    : Visibility.Collapsed;
        PanelProcessSwitch.Visibility = isProcessSwitch   ? Visibility.Visible    : Visibility.Collapsed;

        if (isStandard)
        {
            LblCommand.Content = tag switch
            {
                "OpenFolder" => "Chemin du dossier *",
                "OpenUrl"    => "URL *",
                _            => "Commande *",
            };
            BtnBrowse.Visibility = tag == "OpenUrl" ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (isTerminal)
        {
            // Pré-remplir depuis Entry.Terminal si disponible
            if (Entry.Terminal != null && CmbTerminal.SelectedItem == null)
                RestoreTerminalFields(Entry.Terminal);
            else if (CmbTerminal.SelectedItem == null && _terminals.Count > 0)
                CmbTerminal.SelectedIndex = 0;
        }
        else if (isProcessSwitch)
        {
            if (Entry.ProcessSwitch != null)
            {
                TxtPsExecutable.Text  = Entry.ProcessSwitch.Executable;
                TxtPsProcessName.Text = Entry.ProcessSwitch.ProcessName;
                TxtPsParameters.Text  = Entry.ProcessSwitch.Parameters;
                SetPsSearchMode(Entry.ProcessSwitch.SearchMode);
            }
            else if (CmbPsSearchMode.SelectedItem == null)
            {
                CmbPsSearchMode.SelectedIndex = 0;
            }
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
            Title  = Loc.T("Shortcut_PickTerminal"),
            Filter = Loc.T("Shortcut_PickExe_Filter")
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
            Description        = Loc.T("Shortcut_PickStartFolder"),
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(TxtTerminalDir.Text))
            dlg.InitialDirectory = TxtTerminalDir.Text;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtTerminalDir.Text = dlg.SelectedPath;
    }

    // ── SwitchToProcess ──────────────────────────────────────────────────────

    private void SetPsSearchMode(ProcessSearchMode mode)
    {
        foreach (ComboBoxItem item in CmbPsSearchMode.Items)
        {
            if (item.Tag?.ToString() == mode.ToString())
            {
                CmbPsSearchMode.SelectedItem = item;
                return;
            }
        }
        CmbPsSearchMode.SelectedIndex = 0;
    }

    private void CmbPsSearchMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LblPsSearchTerm == null) return;
        var mode = (CmbPsSearchMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        bool isByTitle = mode == "ByWindowTitle";
        LblPsSearchTerm.Content          = Loc.T(isByTitle ? "Shortcut_WindowTitle" : "Shortcut_ProcessName");
        TxtPsProcessName.ToolTip         = isByTitle
            ? Loc.T("Shortcut_WindowTitle_Tip")
            : Loc.T("Shortcut_ProcessName_Tip");
        TxtPsParameters.ToolTip = isByTitle
            ? Loc.T("Shortcut_Params_Tip_Title")
            : Loc.T("Shortcut_Params_Tip_Name");
    }

    private void BrowsePsExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = Loc.T("Entry_Pick_Exe"),
            Filter = Loc.T("Shortcut_PickExe_Filter")
        };
        if (!string.IsNullOrEmpty(TxtPsExecutable.Text))
        {
            var dir = Path.GetDirectoryName(TxtPsExecutable.Text);
            if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
        }
        if (dlg.ShowDialog() != true) return;
        TxtPsExecutable.Text = dlg.FileName;
    }

    private void PsExecutable_Changed(object sender, TextChangedEventArgs e)
    {
        // Auto-remplir ProcessName depuis le nom de fichier de l'exécutable
        if (TxtPsProcessName == null) return;
        var path = TxtPsExecutable.Text.Trim();
        if (!string.IsNullOrEmpty(path) && string.IsNullOrEmpty(TxtPsProcessName.Text))
            TxtPsProcessName.Text = Path.GetFileName(path);
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
                Description        = Loc.T("Shortcut_PickFolder"),
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
                Title  = Loc.T("Entry_Pick_Exe"),
                Filter = Loc.T("Shortcut_PickExe_Filter")
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
            Title  = Loc.T("Quick_Tile_PickIcon"),
            Filter = Loc.T("Shortcut_PickIcon_Filter")
        };
        var initDir = GetIconInitialDir(Entry.IconPath, Entry.IconProfilePath);
        if (initDir != null) dlg.InitialDirectory = initDir;
        if (dlg.ShowDialog() == true)
            TxtIconPath.Text = dlg.FileName;
    }

    private static string? GetIconInitialDir(string iconPath, string? iconProfilePath)
    {
        if (!string.IsNullOrEmpty(iconPath))
        {
            var dir = Path.GetDirectoryName(iconPath);
            if (Directory.Exists(dir)) return dir;
        }
        var profileAbs = IconStoreService.ResolveProfilePath(iconProfilePath);
        if (!string.IsNullOrEmpty(profileAbs))
        {
            var dir = Path.GetDirectoryName(profileAbs);
            if (Directory.Exists(dir)) return dir;
        }
        return null;
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
        catch (Exception ex) { Services.LogService.Warn(ex, "Aperçu de l'icône (ShortcutDialog)"); }
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
            AppDialog.Warning(Loc.T("Shortcut_Err_NameRequired"), owner: this);
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
                AppDialog.Warning(Loc.T("Shortcut_Err_NoTerminal"), owner: this);
                CmbTerminal.Focus();
                return;
            }
            Entry.Name            = name;
            Entry.Type            = type;
            Entry.Terminal        = cfg;
            Entry.ProcessSwitch   = null;
            Entry.Command         = TxtCmdPreview.Text; // pour le tooltip
            Entry.IconPath        = TxtIconPath.Text.Trim();
            Entry.IconProfilePath = IconStoreService.CopyToProfile(Entry.IconPath);
        }
        else if (type == ShortcutType.SwitchToProcess)
        {
            string exe = TxtPsExecutable.Text.Trim();
            string procName = TxtPsProcessName.Text.Trim();
            var psMode = Enum.TryParse((CmbPsSearchMode.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                out ProcessSearchMode parsedMode) ? parsedMode : ProcessSearchMode.ByProcessName;
            if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(procName))
            {
                string fieldLabel = Loc.T(psMode == ProcessSearchMode.ByWindowTitle
                    ? "Shortcut_FieldLabel_Title" : "Shortcut_FieldLabel_Process");
                AppDialog.Warning(Loc.F("Shortcut_Err_ExeAndField", fieldLabel), owner: this);
                (string.IsNullOrWhiteSpace(exe) ? TxtPsExecutable : TxtPsProcessName).Focus();
                return;
            }
            Entry.Name          = name;
            Entry.Type          = type;
            Entry.Terminal      = null;
            Entry.ProcessSwitch = new ProcessSwitchConfig
            {
                SearchMode  = psMode,
                Executable  = exe,
                ProcessName = procName,
                Parameters  = TxtPsParameters.Text.Trim(),
            };
            Entry.Command         = $"{procName} {Entry.ProcessSwitch.Parameters}".Trim();
            Entry.IconPath        = TxtIconPath.Text.Trim();
            Entry.IconProfilePath = IconStoreService.CopyToProfile(Entry.IconPath);
        }
        else
        {
            string command = TxtCommand.Text.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                AppDialog.Warning(Loc.T("Shortcut_Err_CommandRequired"), owner: this);
                TxtCommand.Focus();
                return;
            }
            Entry.Name            = name;
            Entry.Type            = type;
            Entry.Command         = command;
            Entry.Terminal        = null;
            Entry.ProcessSwitch   = null;
            Entry.IconPath        = TxtIconPath.Text.Trim();
            Entry.IconProfilePath = IconStoreService.CopyToProfile(Entry.IconPath);
        }

        if (string.IsNullOrEmpty(Entry.IconPath) && string.IsNullOrEmpty(Entry.IconProfilePath))
            TryAutoFillIcon();

        DialogResult = true;
        Close();
    }

    private void TryAutoFillIcon()
    {
        string? exePath = Entry.Type switch
        {
            ShortcutType.RunCommand      => ParseExe(Entry.Command),
            ShortcutType.SwitchToProcess => Entry.ProcessSwitch?.Executable,
            ShortcutType.OpenTerminal    => Entry.Terminal?.ExePath,
            _                            => null,
        };

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

        Entry.IconPath        = exePath;
        Entry.IconProfilePath = IconStoreService.CopyToProfile(exePath);
    }

    private static string? ParseExe(string command)
    {
        command = command.Trim();
        string candidate = command.StartsWith('"')
            ? command[1..Math.Max(command.IndexOf('"', 1), 1)]
            : (command.IndexOf(' ') > 0 ? command[..command.IndexOf(' ')] : command);
        return File.Exists(candidate) ? candidate : null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
