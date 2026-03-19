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

namespace WinContextMenuManager;

public partial class QuickAccessWindow : Window
{
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
        var shortcuts = ShortcutService.Load();

        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 6; col++)
            {
                var entry = shortcuts.FirstOrDefault(s => s.Row == row && s.Col == col);
                var btn = entry is { Name.Length: > 0 }
                    ? CreateTile(entry)
                    : CreateEmptyTile(row, col);

                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                ShortcutsGrid.Children.Add(btn);
            }
        }
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
            Tag = entry,
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, label }
            }
        };

        btn.Click += Tile_Click;
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
        var delete = new MenuItem { Header = "🗑 Supprimer" };
        delete.Click += (_, _) => DeleteTile(entry);
        menu.Items.Add(changeIcon);
        menu.Items.Add(new Separator());
        menu.Items.Add(edit);
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
        var existing = all.FirstOrDefault(s => s.Row == entry.Row && s.Col == entry.Col);
        if (existing != null)
        {
            existing.Name     = dlg.Entry.Name;
            existing.Type     = dlg.Entry.Type;
            existing.Command  = dlg.Entry.Command;
            existing.IconPath = dlg.Entry.IconPath;
        }
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void DeleteTile(ShortcutEntry entry)
    {
        var result = MessageBox.Show($"Supprimer « {entry.Name} » ?", "Confirmation",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        var all = ShortcutService.Load();
        all.RemoveAll(s => s.Row == entry.Row && s.Col == entry.Col);
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutEntry entry }) return;
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
                    OpenTerminal(entry.Command);
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

    private static void OpenTerminal(string folder)
    {
        // Essaie wt → pwsh → powershell → cmd
        string[] candidates = ["wt.exe", "pwsh.exe", "powershell.exe", "cmd.exe"];
        foreach (var term in candidates)
        {
            try
            {
                string args = term switch
                {
                    "wt.exe"          => $"-w 0 new-tab --startingDirectory \"{folder}\"",
                    "pwsh.exe"        => $"-NoExit -Command Set-Location \"{folder}\"",
                    "powershell.exe"  => $"-NoExit -Command Set-Location \"{folder}\"",
                    _                 => $"/k cd /d \"{folder}\"",
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
        var existing = all.FirstOrDefault(s => s.Row == entry.Row && s.Col == entry.Col);
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
        if (sender is not Button { Tag: ShortcutEntry entry }) return;

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
        var source = all.FirstOrDefault(s => s.Row == _dragSource.Row && s.Col == _dragSource.Col);
        var target = all.FirstOrDefault(s => s.Row == targetRow       && s.Col == targetCol);

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
