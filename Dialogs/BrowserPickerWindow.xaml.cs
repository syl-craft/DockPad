using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DockPad.Models;
using DockPad.Services;

namespace DockPad;

/// <summary>
/// Popup de choix du navigateur affichée à la réception d'une URL.
/// Clavier : 1-9 = choix direct, ↑/↓ + Entrée = navigation, Échap = annuler.
/// Perte de focus = fermeture sans ouvrir.
/// </summary>
public partial class BrowserPickerWindow : Window
{
    private readonly string _url;
    private readonly BrowsersConfig _config;
    private readonly List<BrowserEntry> _browsers;
    private readonly string? _host;
    private bool _suppressClose = false;

    private sealed record PickerItem(BrowserEntry Entry, System.Windows.Media.ImageSource? Icon,
                                     string Name, string Badge);

    public BrowserPickerWindow(string url, BrowsersConfig config)
    {
        InitializeComponent();

        _url      = url;
        _config   = config;
        _browsers = config.Browsers.Where(b => !b.Hidden).OrderBy(b => b.Order).ToList();
        _host     = UrlRouterService.ExtractHost(url);

        TxtUrl.Text = url;

        ChkAlways.IsEnabled = _host is not null;
        if (_host is not null) ChkAlways.Content = $"Toujours pour {_host}";

        LstBrowsers.ItemsSource = _browsers.Select((b, i) => new PickerItem(
            b,
            LoadIcon(IconCacheService.ResolveProfilePath(b.IconProfilePath)
                     ?? (string.IsNullOrEmpty(b.IconPath) ? b.ExePath : b.IconPath)),
            b.Name,
            i < 9 ? $"{i + 1}" : "")).ToList();
        LstBrowsers.SelectedIndex = _browsers.Count > 0 ? 0 : -1;

        Deactivated += (_, _) => { if (!_suppressClose) Close(); };
        Loaded      += (_, _) => { Activate(); LstBrowsers.Focus(); };
    }

    // ── Interactions ────────────────────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }

        if (e.Key == Key.Enter && LstBrowsers.SelectedItem is PickerItem sel)
        {
            OpenBrowser(sel.Entry);
            e.Handled = true;
            return;
        }

        int index = e.Key switch
        {
            >= Key.D1 and <= Key.D9           => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (index >= 0 && index < _browsers.Count)
        {
            OpenBrowser(_browsers[index]);
            e.Handled = true;
        }
    }

    private void LstBrowsers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstBrowsers.SelectedItem is PickerItem sel) OpenBrowser(sel.Entry);
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_url); } catch { return; }

        BtnCopy.Content = "Copié ✓";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { BtnCopy.Content = "⧉ Copier"; timer.Stop(); };
        timer.Start();
    }

    private void OpenBrowser(BrowserEntry browser)
    {
        if (ChkAlways.IsChecked == true && _host is not null)
        {
            _config.Rules.RemoveAll(r => r.Host == _host);
            _config.Rules.Add(new BrowserRule { Host = _host, BrowserId = browser.Id });
            BrowserConfigService.Save(_config);
        }

        // En cas d'échec de lancement, la popup reste ouverte (AppDialog déjà affiché).
        _suppressClose = true;
        try
        {
            if (UrlRouterService.Launch(browser, _url))
                Close();
            else
            {
                Activate();
                LstBrowsers.Focus();
            }
        }
        finally { _suppressClose = false; }
    }

    // ── Icônes (même pattern que QuickAccessWindow.LoadIcon) ───────────────────

    private static System.Windows.Media.ImageSource? LoadIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        string path = iconPath.Split(',')[0].Trim('"').Trim();
        if (!File.Exists(path)) return null;

        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".exe" or ".dll"))
                return new BitmapImage(new Uri(path));

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
        catch { return null; }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
