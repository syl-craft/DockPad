using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services;
using Microsoft.Win32;

namespace DockPad;

/// <summary>
/// Configuration du sélecteur de navigateur : liste (détection + édition manuelle),
/// règles de domaine, enregistrement comme navigateur. Sauvegarde immédiate à chaque action.
/// </summary>
public partial class BrowserConfigDialog : Window
{
    private BrowsersConfig _config = null!;
    private DateTime _configWriteTimeUtc;

    private sealed record BrowserItem(BrowserEntry Entry, System.Windows.Media.ImageSource? Icon,
                                      string Name, string Detail, string HiddenLabel);
    private sealed record RuleItem(BrowserRule Rule, string Host, string BrowserName);

    public BrowserConfigDialog()
    {
        InitializeComponent();
        _config = BrowserConfigService.EnsureInitialized();
        _configWriteTimeUtc = GetConfigWriteTimeUtc();
        RefreshBrowsers();
        RefreshRules();
        RefreshRegistrationStatus();

        Activated += Window_Activated;
    }

    // ── Rafraîchissement croisé avec le picker ─────────────────────────────────

    /// <summary>
    /// Le picker de navigateur (popup au clic sur une URL) peut sauvegarder une nouvelle
    /// règle pendant que ce dialog est ouvert. Comme il vole l'activation, le retour au
    /// dialog déclenche Activated : on recharge le snapshot uniquement si le fichier a
    /// changé depuis notre dernier Load/Save, pour ne pas perdre l'édition en cours pour rien.
    /// </summary>
    private void Window_Activated(object? sender, EventArgs e)
    {
        var writeTime = GetConfigWriteTimeUtc();
        if (writeTime <= _configWriteTimeUtc) return;

        _config = BrowserConfigService.Load();
        _configWriteTimeUtc = writeTime;
        RefreshBrowsers();
        RefreshRules();
        RefreshRegistrationStatus();
    }

    private static DateTime GetConfigWriteTimeUtc() =>
        File.Exists(BrowserConfigService.FilePath)
            ? File.GetLastWriteTimeUtc(BrowserConfigService.FilePath)
            : DateTime.MinValue;

    // ── Rafraîchissement ────────────────────────────────────────────────────────

    private void RefreshBrowsers(string? selectId = null)
    {
        var ordered = _config.Browsers.OrderBy(b => b.Order).ToList();
        LstBrowsers.ItemsSource = ordered.Select(b => new BrowserItem(
            b,
            LoadIcon(IconCacheService.ResolveProfilePath(b.IconProfilePath)
                     ?? (string.IsNullOrEmpty(b.IconPath) ? b.ExePath : b.IconPath)),
            b.Name,
            b.ExePath + (string.IsNullOrWhiteSpace(b.Arguments) ? "" : " " + b.Arguments),
            b.Hidden ? "masqué" : "")).ToList();

        if (selectId is not null)
            LstBrowsers.SelectedIndex = ordered.FindIndex(b => b.Id == selectId);
    }

    private void RefreshRules()
    {
        LstRules.ItemsSource = _config.Rules.Select(r => new RuleItem(
            r, r.Host,
            _config.Browsers.FirstOrDefault(b => b.Id == r.BrowserId)?.Name ?? "?")).ToList();
    }

    private void RefreshRegistrationStatus()
    {
        var status = BrowserRegistrationService.GetStatus();
        (TxtRegStatus.Text, BtnRegister.IsEnabled) = status switch
        {
            BrowserRegistrationStatus.Default =>
                ("✔ DockPad est le navigateur par défaut : les URLs cliquées affichent la popup de choix.", false),
            BrowserRegistrationStatus.Registered =>
                ("DockPad est enregistré comme navigateur. Pour intercepter les URLs, choisis-le comme " +
                 "navigateur par défaut dans les paramètres Windows (bouton ci-dessous).", false),
            _ =>
                ("DockPad n'est pas enregistré comme navigateur. Enregistre-le puis choisis-le comme " +
                 "navigateur par défaut dans les paramètres Windows.", true),
        };
    }

    private void Save()
    {
        BrowserConfigService.Save(_config);
        _configWriteTimeUtc = GetConfigWriteTimeUtc();
    }

    private BrowserEntry? Selected => (LstBrowsers.SelectedItem as BrowserItem)?.Entry;

    // ── Enregistrement ──────────────────────────────────────────────────────────

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BrowserRegistrationService.Register();
        }
        catch (Exception ex)
        {
            AppDialog.Error($"Impossible d'enregistrer DockPad comme navigateur :\n{ex.Message}", owner: this);
            return;
        }
        RefreshRegistrationStatus();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        BrowserRegistrationService.OpenDefaultAppsSettings();

    // ── Liste des navigateurs ───────────────────────────────────────────────────

    private void LstBrowsers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var b = Selected;
        TxtName.Text = b?.Name ?? "";
        TxtExe.Text  = b?.ExePath ?? "";
        TxtArgs.Text = b?.Arguments ?? "";
    }

    private void Redetect_Click(object sender, RoutedEventArgs e)
    {
        int added = 0;
        foreach (var found in BrowserDetectionService.Detect())
        {
            if (_config.Browsers.Any(b => string.Equals(b.ExePath, found.ExePath, StringComparison.OrdinalIgnoreCase)))
                continue;
            found.Order = _config.Browsers.Count == 0 ? 0 : _config.Browsers.Max(b => b.Order) + 1;
            found.IconProfilePath = IconCacheService.CopyToProfile(found.ExePath);
            _config.Browsers.Add(found);
            added++;
        }
        if (added > 0) { Save(); RefreshBrowsers(); }
        AppDialog.Info(added > 0 ? $"{added} navigateur(s) ajouté(s)." : "Aucun nouveau navigateur détecté.",
                       owner: this);
    }

    private void Up_Click(object sender, RoutedEventArgs e)   => MoveSelected(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        var b = Selected;
        if (b is null) return;

        var ordered = _config.Browsers.OrderBy(x => x.Order).ToList();
        int i = ordered.IndexOf(b), j = i + delta;
        if (j < 0 || j >= ordered.Count) return;

        (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        for (int k = 0; k < ordered.Count; k++) ordered[k].Order = k;

        Save();
        RefreshBrowsers(selectId: b.Id);
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        var b = Selected;
        if (b is null) return;
        b.Hidden = !b.Hidden;
        Save();
        RefreshBrowsers(selectId: b.Id);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var b = Selected;
        if (b is null) return;
        if (!AppDialog.Confirm($"Supprimer « {b.Name} » ?\nLes règles de domaine associées seront supprimées aussi.",
                               owner: this))
            return;

        _config.Browsers.Remove(b);
        _config.Rules.RemoveAll(r => r.BrowserId == b.Id);
        Save();
        RefreshBrowsers();
        RefreshRules();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var entry = new BrowserEntry { Name = "Nouveau navigateur" };
        entry.Order = _config.Browsers.Count == 0 ? 0 : _config.Browsers.Max(b => b.Order) + 1;
        _config.Browsers.Add(entry);
        Save();
        RefreshBrowsers(selectId: entry.Id);
        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Exécutables (*.exe)|*.exe", CheckFileExists = true };
        if (dlg.ShowDialog(this) == true) TxtExe.Text = dlg.FileName;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var b = Selected;
        if (b is null) return;

        b.Name      = TxtName.Text.Trim();
        b.ExePath   = TxtExe.Text.Trim().Trim('"');
        b.Arguments = TxtArgs.Text.Trim();

        // Icône : (ré)extraite de l'exe si aucune icône profil ou si l'exe a changé.
        var cached = IconCacheService.CopyToProfile(b.ExePath);
        if (cached is not null) b.IconProfilePath = cached;

        Save();
        RefreshBrowsers(selectId: b.Id);
    }

    // ── Règles ──────────────────────────────────────────────────────────────────

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (LstRules.SelectedItem is not RuleItem item) return;
        _config.Rules.Remove(item.Rule);
        Save();
        RefreshRules();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Icônes (même pattern que BrowserPickerWindow) ───────────────────────────

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
