using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinContextMenuManager.Models;
using WinContextMenuManager.Services;

namespace WinContextMenuManager;

public partial class QuickAccessWindow : Window
{
    public QuickAccessWindow()
    {
        InitializeComponent();
        PopulateGrid();
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
                    : CreateEmptyTile();

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
            ToolTip = entry.Command,
            Tag = entry,
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, label }
            }
        };

        btn.Click += Tile_Click;
        return btn;
    }

    private Button CreateEmptyTile() => new()
    {
        Style = (Style)FindResource("EmptyTileButton"),
        Content = new TextBlock
        {
            Text = "+",
            FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        }
    };

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutEntry entry }) return;
        try
        {
            var (exe, args) = ParseCommand(entry.Command);
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'exécuter la commande :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
