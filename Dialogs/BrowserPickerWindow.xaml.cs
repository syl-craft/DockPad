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
    private readonly List<PickerItem> _items;    // toutes les lignes, en-têtes de groupe compris
    private readonly List<PickerItem> _choices;  // lignes réellement choisissables
    private readonly string? _host;
    private bool _suppressClose = false;
    private bool _closing = false;

    private DispatcherTimer? _autoOpenTimer;
    private int _autoOpenRemaining;

    /// <summary>Ligne de la liste. INotifyPropertyChanged pour le décompte sur le badge n°1.</summary>
    private sealed class PickerItem : System.ComponentModel.INotifyPropertyChanged
    {
        public required BrowserEntry Entry { get; init; }
        public System.Windows.Media.ImageSource? Icon { get; init; }
        public string Name { get; init; } = "";

        /// <summary>Profil : ligne indentée sous son navigateur.</summary>
        public bool IsChild { get; init; }

        /// <summary>Navigateur masqué gardé comme titre de groupe : ni choisissable ni numéroté.</summary>
        public bool IsHeader { get; init; }

        /// <summary>Navigateur qui a des profils visibles en dessous.</summary>
        public bool IsGroupTitle { get; init; }

        private string _badge = "";
        public string Badge { get => _badge; set { _badge = value; Notify(nameof(Badge)); } }

        private bool _isCountdown;
        public bool IsCountdown { get => _isCountdown; set { _isCountdown = value; Notify(nameof(IsCountdown)); } }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
    }

    public BrowserPickerWindow(string url, BrowsersConfig config)
    {
        InitializeComponent();

        _url      = url;
        _config   = config;
        _host     = UrlRouterService.ExtractHost(url);

        TxtUrl.Text = url;

        ChkAlways.IsEnabled = _host is not null;
        if (_host is not null) ChkAlways.Content = Loc.F("Picker_AlwaysForHost", _host);

        // Navigateurs et leurs profils : les badges 1-9 ne numérotent que les lignes
        // choisissables (un titre de groupe n'en reçoit pas).
        _items = [];
        foreach (var row in BrowserRowLayout.ForPicker(config))
        {
            int badge = _items.Count(i => !i.IsHeader) + 1;
            _items.Add(new PickerItem
            {
                Entry = row.Entry,
                Icon  = LoadIcon(IconStoreService.ResolveProfilePath(row.Entry.IconProfilePath)
                                 ?? (string.IsNullOrEmpty(row.Entry.IconPath) ? row.Entry.ExePath : row.Entry.IconPath)),
                Name  = row.Entry.Name,
                IsChild  = row.IsChild,
                IsHeader = row.IsHeader,
                IsGroupTitle = !row.IsChild && !row.IsHeader
                               && BrowserRowLayout.Children(config, row.Entry.Id).Any(c => !c.Hidden),
                Badge = !row.IsHeader && badge <= 9 ? $"{badge}" : "",
            });
        }
        _choices = _items.Where(i => !i.IsHeader).ToList();

        LstBrowsers.ItemsSource = _items;
        LstBrowsers.SelectedIndex = _choices.Count > 0 ? _items.IndexOf(_choices[0]) : -1;

        // Garde _closing : la fermeture (Échap…) désactive la fenêtre → WM_ACTIVATE →
        // Deactivated pendant InternalClose ; rappeler Close() à ce moment lève
        // InvalidOperationException et tue le process (crash constaté en 1.6.1).
        Deactivated += (_, _) => { if (!_suppressClose && !_closing) Close(); };
        Loaded      += (_, _) => { Activate(); LstBrowsers.Focus(); };
        Closed      += (_, _) => _autoOpenTimer?.Stop();

        if (config.AutoOpenSeconds > 0 && _choices.Count > 0)
            StartAutoOpen(config.AutoOpenSeconds);
    }

    // ── Ouverture automatique ───────────────────────────────────────────────────

    private void StartAutoOpen(int seconds)
    {
        _autoOpenRemaining = seconds;
        UpdateAutoOpenDisplay();

        _autoOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoOpenTimer.Tick += (_, _) =>
        {
            _autoOpenRemaining--;
            if (_autoOpenRemaining <= 0)
            {
                CancelAutoOpen();
                OpenBrowser(_choices[0].Entry);
            }
            else
            {
                UpdateAutoOpenDisplay();
            }
        };
        _autoOpenTimer.Start();

        // Première interaction (clavier, clic, molette) → annulation du décompte.
        PreviewKeyDown   += CancelAutoOpenOnInteraction;
        PreviewMouseDown += CancelAutoOpenOnInteraction;
        PreviewMouseWheel += CancelAutoOpenOnInteraction;
    }

    private void CancelAutoOpenOnInteraction(object? sender, InputEventArgs e) => CancelAutoOpen();

    private void CancelAutoOpen()
    {
        if (_autoOpenTimer is null) return;

        _autoOpenTimer.Stop();
        _autoOpenTimer = null;
        PreviewKeyDown   -= CancelAutoOpenOnInteraction;
        PreviewMouseDown -= CancelAutoOpenOnInteraction;
        PreviewMouseWheel -= CancelAutoOpenOnInteraction;

        if (_choices.Count > 0)
        {
            _choices[0].IsCountdown = false;
            _choices[0].Badge = "1";
        }
        TxtCountdown.Visibility = Visibility.Collapsed;
    }

    private void UpdateAutoOpenDisplay()
    {
        _choices[0].IsCountdown = true;
        _choices[0].Badge = $"{_autoOpenRemaining}s";
        TxtCountdown.Text = Loc.F("Picker_AutoOpenIn", _autoOpenRemaining);
        TxtCountdown.Visibility = Visibility.Visible;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
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
        if (index >= 0 && index < _choices.Count)
        {
            OpenBrowser(_choices[index].Entry);
            e.Handled = true;
        }
    }

    private void LstBrowsers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstBrowsers.SelectedItem is PickerItem sel) OpenBrowser(sel.Entry);
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_url); } catch (Exception ex) { Services.LogService.Warn(ex, "Copie de l'URL dans le presse-papiers"); return; }

        BtnCopy.Content = Loc.T("Picker_Copied");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { BtnCopy.Content = Loc.T("Picker_Copy"); timer.Stop(); };
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
        catch (Exception ex) { Services.LogService.Warn(ex, "Chargement d'une icône de navigateur (picker)"); return null; }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
