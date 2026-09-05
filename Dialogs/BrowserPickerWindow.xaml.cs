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

        // L'état réel, lu sur le fichier des favoris : un toggle qui montre un état doit dire la
        // vérité, sinon il n'est qu'un bouton déguisé. Posé avant tout affichage, et sans passer
        // par Checked/Unchecked — c'est Click qui déclenche l'écriture, donc seule une action de
        // l'utilisateur écrit.
        BtnFavorite.IsChecked = FavoriteToggle.Find(
            ShortcutService.Load(TileStore.EntriesPath(Models.TileTarget.Favorites)), url) is not null;
        UpdateFavoriteHints();

        // Navigateurs et leurs profils : les badges 1-9 ne numérotent que les lignes
        // choisissables (un titre de groupe n'en reçoit pas).
        _items = [];
        foreach (var row in BrowserRowLayout.ForPicker(config))
        {
            int badge = _items.Count(i => !i.IsHeader) + 1;
            _items.Add(new PickerItem
            {
                Entry = row.Entry,
                Icon  = IconStoreService.LoadImage(IconStoreService.ResolveProfilePath(row.Entry.IconProfilePath)
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

        ButtonFlash.Flash(BtnCopy, Loc.T("Picker_Copied"), TimeSpan.FromSeconds(1.5));
    }

    /// <summary>
    /// Met la page en favori, ou l'en retire. Le clic a déjà basculé <c>IsChecked</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Click</c> et non <c>Checked</c>/<c>Unchecked</c> : ces deux-là se déclenchent aussi quand
    /// le code pose l'état initial, ce qui ferait réécrire le favori à chaque ouverture du popup.
    /// </para>
    /// <para>
    /// <b>Aucune fenêtre n'est supposée exister.</b> Au clic sur un lien, DockPad tourne souvent en
    /// arrière-plan sans grille affichée — l'écriture passe par le service, et la grille se
    /// rafraîchit seulement si elle est là, par le même point que les mutations MCP.
    /// </para>
    /// </remarks>
    private async void BtnFavorite_Click(object sender, RoutedEventArgs e)
    {
        bool wanted = BtnFavorite.IsChecked == true;

        // Le décompte est déjà annulé par PreviewMouseDown : sans ça, mettre en favori aurait
        // ouvert le navigateur sous les doigts.
        BtnFavorite.IsEnabled = false;
        try
        {
            bool actual = await FavoriteToggle.SetAsync(_url, wanted);
            BtnFavorite.IsChecked = actual;
            UpdateFavoriteHints();
            McpDispatcher.OnMutation?.Invoke();
        }
        catch (Exception ex)
        {
            // Un favori qui ne s'enregistre pas ne doit pas emporter la popup : on est là pour
            // ouvrir un lien. L'étoile revient à l'état d'avant, qui est la vérité du fichier.
            LogService.Warn(ex, "Enregistrement d'un favori depuis la popup");
            BtnFavorite.IsChecked = !wanted;
            UpdateFavoriteHints();
        }
        finally { BtnFavorite.IsEnabled = true; }
    }

    /// <summary>
    /// Infobulle et nom d'accessibilité de l'étoile : ils nomment l'action, là où le glyphe du
    /// Style dit l'état. Posés en code parce qu'ils suivent l'état — un <c>{loc:T}</c> serait
    /// une liaison que cette affectation remplacerait définitivement.
    /// </summary>
    private void UpdateFavoriteHints()
    {
        var text = Loc.T(BtnFavorite.IsChecked == true ? "Picker_Favorite_Remove" : "Picker_Favorite_Add");
        BtnFavorite.ToolTip = text;
        System.Windows.Automation.AutomationProperties.SetName(BtnFavorite, text);
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
}
