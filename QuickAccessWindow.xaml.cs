using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinContextMenuManager.Models;
using WinContextMenuManager.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace WinContextMenuManager;

public partial class QuickAccessWindow : Window
{
    private const int GridRows = 4;
    private const int GridCols = 6;

    private int _currentPage = 0;

    private IntPtr _hwnd;
    private Point _dragStartPoint;
    private ShortcutEntry? _dragSource;

    public QuickAccessWindow()
    {
        InitializeComponent();
        PopulateGrid();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        RegisterHotkey();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        UnregisterHotkey();
        base.OnClosing(e);
    }

    private void RegisterHotkey()
    {
        var (mods, key) = SettingsService.LoadHotkey();
        HotkeyService.RegisterHotKey(_hwnd, HotkeyService.HotkeyId, mods | HotkeyService.MOD_NOREPEAT, key);
    }

    private void UnregisterHotkey()
    {
        HotkeyService.UnregisterHotKey(_hwnd, HotkeyService.HotkeyId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyService.WM_HOTKEY && wParam.ToInt32() == HotkeyService.HotkeyId)
        {
            WindowState = WindowState.Normal;
            Show();
            Activate();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Presets_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PresetsDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        UnregisterHotkey();
        RegisterHotkey();
    }

    private void PopulateGrid()
    {
        ShortcutsGrid.Children.Clear();
        var all = ShortcutService.Load();

        UpdatePagination(all);

        var pageEntries = all.Where(s => s.Page == _currentPage).ToList();

        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                var entry = pageEntries.FirstOrDefault(s => s.Row == row && s.Col == col);
                var btn = entry is { Name.Length: > 0 }
                    ? CreateTile(entry)
                    : CreateEmptyTile(row, col);

                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                ShortcutsGrid.Children.Add(btn);
            }
        }
    }

    private void UpdatePagination(List<ShortcutEntry> all)
    {
        PaginationBar.Children.Clear();

        int maxPage = all.Count > 0 ? all.Max(s => s.Page) : 0;
        int totalPages = maxPage + 2; // toujours une page vide supplémentaire à la fin

        for (int p = 0; p < totalPages; p++)
        {
            int page = p;
            var btn = new Button
            {
                Content = (page + 1).ToString(),
                Style   = (Style)FindResource(page == _currentPage ? "PageButtonActive" : "PageButton"),
                ToolTip = $"Page {page + 1}",
            };
            btn.Click += (_, _) => GoToPage(page);
            PaginationBar.Children.Add(btn);
        }
    }

    private void GoToPage(int page)
    {
        _currentPage = page;
        PopulateGrid();
    }

    private Button CreateTile(ShortcutEntry entry)
    {
        var icon = new Image
        {
            Width = 36,
            Height = 36,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 6),
            Source = LoadIcon(entry.IconPath)
        };

        var label = new TextBlock
        {
            Text = entry.Name,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 94,
            FontSize = 11,
        };

        var btn = new Button
        {
            Style = (Style)FindResource("TileButton"),
            ToolTip = $"[{TypeLabel(entry.Type)}] {entry.Command}",
            DataContext = entry,
            Tag = TypeBandBrush(entry.Type),
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, label }
            }
        };

        btn.Click += Tile_Click;
        btn.MouseEnter += TileHover_Enter;
        btn.MouseLeave += TileHover_Leave;
        btn.PreviewMouseLeftButtonDown += TileDrag_MouseDown;
        btn.PreviewMouseMove += TileDrag_MouseMove;
        btn.AllowDrop = true;
        btn.DragOver  += TileDrop_DragOver;
        btn.DragLeave += TileDrop_DragLeave;
        btn.Drop      += TileDrop_Drop;

        var menu = new ContextMenu();
        var changeIcon = new MenuItem { Header = "🖼 Changer l'icône" };
        changeIcon.Click += (_, _) => ChangeIcon(btn, entry);
        var edit = new MenuItem { Header = "✏ Modifier" };
        edit.Click += (_, _) => EditTile(entry);
        var duplicate = new MenuItem { Header = "⧉ Dupliquer" };
        duplicate.Click += (_, _) => DuplicateTile(entry);
        var delete = new MenuItem { Header = "🗑 Supprimer" };
        delete.Click += (_, _) => DeleteTile(entry);
        menu.Items.Add(changeIcon);
        menu.Items.Add(new Separator());
        menu.Items.Add(edit);
        menu.Items.Add(duplicate);
        menu.Items.Add(delete);
        btn.ContextMenu = menu;

        return btn;
    }

    private Button CreateEmptyTile(int row, int col)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("EmptyTileButton"),
            Content = new TextBlock
            {
                Text = "+",
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            }
        };
        btn.AllowDrop = true;
        btn.DragOver  += TileDrop_DragOver;
        btn.DragLeave += TileDrop_DragLeave;
        btn.Drop      += TileDrop_Drop;

        var menu = new ContextMenu();
        var add = new MenuItem { Header = "➕ Ajouter" };
        add.Click += (_, _) => AddTile(row, col);
        menu.Items.Add(add);
        btn.ContextMenu = menu;

        return btn;
    }

    private void AddTile(int row, int col)
    {
        var dlg = new ShortcutDialog(row: row, col: col) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        dlg.Entry.Page = _currentPage;
        var all = ShortcutService.Load();
        all.Add(dlg.Entry);
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void EditTile(ShortcutEntry entry)
    {
        var dlg = new ShortcutDialog(entry) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var all = ShortcutService.Load();
        var existing = all.FirstOrDefault(s => s.Page == entry.Page && s.Row == entry.Row && s.Col == entry.Col);
        if (existing != null)
        {
            existing.Name     = dlg.Entry.Name;
            existing.Type     = dlg.Entry.Type;
            existing.Command  = dlg.Entry.Command;
            existing.IconPath = dlg.Entry.IconPath;
            existing.Terminal = dlg.Entry.Terminal;
        }
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void DuplicateTile(ShortcutEntry entry)
    {
        var all = ShortcutService.Load();
        var occupied = all
            .Where(s => s.Page == _currentPage)
            .Select(s => (s.Row, s.Col))
            .ToHashSet();

        // Case vide la plus proche sur la page courante (distance de Chebyshev)
        (int row, int col)? nearest = null;
        int bestDist = int.MaxValue;
        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                if (occupied.Contains((r, c))) continue;
                int dist = Math.Max(Math.Abs(r - entry.Row), Math.Abs(c - entry.Col));
                if (dist < bestDist) { bestDist = dist; nearest = (r, c); }
            }
        }

        if (nearest is null)
        {
            MessageBox.Show("Page pleine. Naviguez vers une autre page pour dupliquer.",
                "Dupliquer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        all.Add(new ShortcutEntry
        {
            Page     = _currentPage,
            Row      = nearest.Value.row, Col = nearest.Value.col,
            Name     = entry.Name,    Type     = entry.Type,
            Command  = entry.Command, IconPath = entry.IconPath,
            Terminal = entry.Terminal == null ? null : new TerminalConfig
            {
                ExePath           = entry.Terminal.ExePath,
                StartingDirectory = entry.Terminal.StartingDirectory,
                RunCommand        = entry.Terminal.RunCommand,
                NewTab            = entry.Terminal.NewTab,
                ExtraArgs         = entry.Terminal.ExtraArgs,
            },
        });
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void DeleteTile(ShortcutEntry entry)
    {
        var result = MessageBox.Show($"Supprimer « {entry.Name} » ?", "Confirmation",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var all = ShortcutService.Load();
        all.RemoveAll(s => s.Page == entry.Page && s.Row == entry.Row && s.Col == entry.Col);
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not ShortcutEntry entry) return;
        try
        {
            switch (entry.Type)
            {
                case ShortcutType.OpenFolder:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{entry.Command}\"")
                        { UseShellExecute = true });
                    break;
                case ShortcutType.OpenUrl:
                    Process.Start(new ProcessStartInfo(entry.Command) { UseShellExecute = true });
                    break;
                case ShortcutType.OpenTerminal:
                    ExecuteTerminal(entry);
                    break;
                case ShortcutType.RunCommand:
                default:
                    var (exe, args) = ParseCommand(entry.Command);
                    Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'exécuter :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void ExecuteTerminal(ShortcutEntry entry)
    {
        if (entry.Terminal is { ExePath.Length: > 0 } cfg)
        {
            var args = TerminalDetectionService.BuildArgs(cfg);
            Process.Start(new ProcessStartInfo(cfg.ExePath, args) { UseShellExecute = true });
            return;
        }

        // Fallback legacy : entry.Command = chemin du dossier, auto-détection du terminal
        string folder = entry.Command;
        foreach (var term in new[] { "wt.exe", "pwsh.exe", "powershell.exe", "cmd.exe" })
        {
            try
            {
                string args = term switch
                {
                    "wt.exe"         => $"-w 0 new-tab --startingDirectory \"{folder}\"",
                    "pwsh.exe"       => $"-NoExit -Command Set-Location \"{folder}\"",
                    "powershell.exe" => $"-NoExit -Command Set-Location \"{folder}\"",
                    _                => $"/k cd /d \"{folder}\"",
                };
                Process.Start(new ProcessStartInfo(term, args) { UseShellExecute = true });
                return;
            }
            catch { }
        }
        throw new InvalidOperationException("Aucun terminal trouvé (wt, pwsh, powershell, cmd).");
    }

    private static string TypeLabel(ShortcutType t) => t switch
    {
        ShortcutType.OpenFolder   => "Dossier",
        ShortcutType.OpenUrl      => "Navigateur",
        ShortcutType.OpenTerminal => "Terminal",
        _                         => "Commande",
    };

    private static readonly SolidColorBrush TileDefaultBackground = new(Colors.White);

    private void TileHover_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: SolidColorBrush band } btn) return;
        var c = band.Color;
        btn.Background   = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B));
        btn.BorderBrush  = band;
    }

    private void TileHover_Leave(object sender, MouseEventArgs e)
    {
        if (sender is not Button btn) return;
        btn.Background  = TileDefaultBackground;
        btn.BorderBrush = DefaultBorder;
    }

    private static readonly SolidColorBrush BandRunCommand   = new(Color.FromRgb(0xA8, 0xCC, 0xEA)); // bleu pastel
    private static readonly SolidColorBrush BandOpenFolder   = new(Color.FromRgb(0xF5, 0xCC, 0x80)); // ambre pastel
    private static readonly SolidColorBrush BandOpenUrl      = new(Color.FromRgb(0x92, 0xC6, 0x90)); // vert pastel
    private static readonly SolidColorBrush BandOpenTerminal = new(Color.FromRgb(0xC4, 0xAD, 0xE0)); // violet pastel

    private static SolidColorBrush TypeBandBrush(ShortcutType t) => t switch
    {
        ShortcutType.OpenFolder   => BandOpenFolder,
        ShortcutType.OpenUrl      => BandOpenUrl,
        ShortcutType.OpenTerminal => BandOpenTerminal,
        _                         => BandRunCommand,
    };

    private void ChangeIcon(Button btn, ShortcutEntry entry)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir une icône",
            Filter = "Images et exécutables|*.png;*.ico;*.bmp;*.jpg;*.jpeg;*.exe;*.dll|Tous les fichiers|*.*"
        };

        if (!string.IsNullOrEmpty(entry.IconPath) && File.Exists(entry.IconPath))
            dlg.InitialDirectory = Path.GetDirectoryName(entry.IconPath);

        if (dlg.ShowDialog() != true) return;

        entry.IconPath = dlg.FileName;

        var all = ShortcutService.Load();
        var existing = all.FirstOrDefault(s => s.Page == entry.Page && s.Row == entry.Row && s.Col == entry.Col);
        if (existing != null)
            existing.IconPath = dlg.FileName;
        ShortcutService.Save(all);

        if (btn.Content is StackPanel sp && sp.Children[0] is Image img)
            img.Source = LoadIcon(dlg.FileName);
    }

    private void TileDrag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void TileDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not Button dragBtn || dragBtn.DataContext is not ShortcutEntry entry) return;

        var pos  = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragSource = entry;
        DragDrop.DoDragDrop((Button)sender, entry, DragDropEffects.Move);
        _dragSource = null;
    }

    private static readonly Brush DragOverBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly Brush DefaultBorder  = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private void TileDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _dragSource != null ? DragDropEffects.Move : DragDropEffects.None;
        if (sender is Button btn) btn.BorderBrush = DragOverBrush;
        e.Handled = true;
    }

    private void TileDrop_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button btn) btn.BorderBrush = DefaultBorder;
    }

    private void TileDrop_Drop(object sender, DragEventArgs e)
    {
        if (_dragSource == null || sender is not Button targetBtn) return;

        if (sender is Button b) b.BorderBrush = DefaultBorder;

        int targetRow = Grid.GetRow(targetBtn);
        int targetCol = Grid.GetColumn(targetBtn);
        if (_dragSource.Row == targetRow && _dragSource.Col == targetCol) return;

        var all    = ShortcutService.Load();
        var source = all.FirstOrDefault(s => s.Page == _currentPage && s.Row == _dragSource.Row && s.Col == _dragSource.Col);
        var target = all.FirstOrDefault(s => s.Page == _currentPage && s.Row == targetRow       && s.Col == targetCol);

        if (source == null) return;

        if (target != null)
            (source.Row, source.Col, target.Row, target.Col) =
            (target.Row, target.Col, source.Row, source.Col);
        else
            (source.Row, source.Col) = (targetRow, targetCol);

        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => PopulateGrid();

    private void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        ShortcutService.OpenInEditor();
    }

    private static (string exe, string args) ParseCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0)
                return (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space > 0
            ? (command[..space], command[(space + 1)..])
            : (command, "");
    }

    private static BitmapSource? LoadIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        try
        {
            string path = iconPath.Split(',')[0].Trim('"').Trim();
            if (!File.Exists(path)) return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".ico" or ".png" or ".bmp" or ".jpg" or ".jpeg")
                return new BitmapImage(new Uri(path));

            if (ext is ".exe" or ".dll")
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) return null;
                using var bmp = icon.ToBitmap();
                var handle = bmp.GetHbitmap();
                try
                {
                    return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        handle, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally { DeleteObject(handle); }
            }
        }
        catch { }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
