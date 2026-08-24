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
                                      string Name, string Detail, string HiddenLabel, bool Visible,
                                      bool IsChild);
    private sealed record RuleItem(BrowserRule Rule, string Host, List<RuleFilterOption> Browsers);
    private sealed record RuleFilterOption(string? Id, string Name);

    private bool _refreshingRuleFilter;

    public BrowserConfigDialog()
    {
        InitializeComponent();
        TxtVersion.Text = AppInfo.VersionText;
        _config = BrowserConfigService.EnsureInitialized();
        _configWriteTimeUtc = GetConfigWriteTimeUtc();
        TxtAutoOpen.Text = _config.AutoOpenSeconds.ToString();
        RefreshBrowsers();
        RefreshRules();
        RefreshRegistrationStatus();

        Activated += Window_Activated;
    }

    /// <summary>Délai d'ouverture automatique du picker (0-300 s, 0 = désactivé), sauvegarde immédiate.</summary>
    private void AutoOpen_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtAutoOpen.Text.Trim(), out int seconds) || seconds < 0) return;
        seconds = Math.Min(seconds, 300);
        if (seconds == _config.AutoOpenSeconds) return;

        _config.AutoOpenSeconds = seconds;
        Save();
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
        TxtAutoOpen.Text = _config.AutoOpenSeconds.ToString();
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
        // Chaque navigateur suivi de ses profils (lignes indentées).
        var rows = BrowserRowLayout.Grouped(_config);
        LstBrowsers.ItemsSource = rows.Select(r => new BrowserItem(
            r.Entry,
            IconStoreService.LoadImage(IconStoreService.ResolveProfilePath(r.Entry.IconProfilePath)
                     ?? (string.IsNullOrEmpty(r.Entry.IconPath) ? r.Entry.ExePath : r.Entry.IconPath)),
            r.Entry.Name,
            Detail(r.Entry),
            r.Entry.Hidden ? Loc.T("Browsers_Badge_Hidden") : "",
            !r.Entry.Hidden,
            r.IsChild)).ToList();

        if (selectId is not null)
            LstBrowsers.SelectedIndex = rows.FindIndex(r => r.Entry.Id == selectId);
    }

    /// <summary>Ligne de détail : arguments réellement passés pour un profil, chemin exe sinon.</summary>
    private static string Detail(BrowserEntry b)
    {
        var args = string.IsNullOrWhiteSpace(b.Arguments) ? "" : " " + b.Arguments;
        return b.ProfileDirectory is { Length: > 0 } dir
            ? $"--profile-directory=\"{dir}\"{args}"
            : b.ExePath + args;
    }

    private void RefreshRules()
    {
        // (Re)peuple le filtre navigateur en préservant la sélection courante.
        _refreshingRuleFilter = true;
        var selectedFilter = CmbRuleFilter.SelectedValue as string;
        var options = new List<RuleFilterOption> { new(null, Loc.T("Browsers_Rules_FilterAll")) };
        options.AddRange(BrowserOptions());
        CmbRuleFilter.ItemsSource = options;
        CmbRuleFilter.SelectedValue = selectedFilter is not null && options.Any(o => o.Id == selectedFilter)
            ? selectedFilter : null;
        if (CmbRuleFilter.SelectedIndex < 0) CmbRuleFilter.SelectedIndex = 0;
        _refreshingRuleFilter = false;

        string search = TxtRuleSearch.Text.Trim();
        string? browserFilter = CmbRuleFilter.SelectedValue as string;
        var browsers = BrowserOptions();

        var filtered = _config.Rules
            .Where(r => search.Length == 0 || r.Host.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(r => browserFilter is null || r.BrowserId == browserFilter)
            .OrderBy(r => r.Host)
            .Select(r => new RuleItem(r, r.Host, browsers))
            .ToList();

        LstRules.ItemsSource = filtered;
        TxtRulesEmpty.Visibility = _config.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtRulesCount.Text = filtered.Count == _config.Rules.Count
            ? Loc.F("Browsers_RuleCount", _config.Rules.Count)
            : Loc.F("Browsers_RuleCountFiltered", _config.Rules.Count, filtered.Count);
    }

    /// <summary>
    /// Choix proposés pour associer une règle : navigateurs et profils, dans l'ordre
    /// d'affichage, un profil libellé « Chrome › Boulot » (hors contexte de liste indentée).
    /// </summary>
    private List<RuleFilterOption> BrowserOptions() =>
        BrowserRowLayout.Grouped(_config)
            .Select(r => new RuleFilterOption(r.Entry.Id, BrowserRowLayout.DisplayName(_config, r.Entry)))
            .ToList();

    /// <summary>Recherche ou filtre modifié (TextChanged + SelectionChanged, via contravariance).</summary>
    private void RuleFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshingRuleFilter || !IsLoaded) return;
        RefreshRules();
    }

    /// <summary>Changement du navigateur associé à une règle, directement dans la ligne.</summary>
    private void RuleBrowser_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cmb || cmb.DataContext is not RuleItem item) return;
        if (cmb.SelectedValue is not string id || id == item.Rule.BrowserId) return;

        item.Rule.BrowserId = id;
        Save();
    }

    private void RefreshRegistrationStatus()
    {
        var status = BrowserRegistrationService.GetStatus();
        (TxtRegStatus.Text, BtnRegister.IsEnabled) = status switch
        {
            BrowserRegistrationStatus.Default => (Loc.T("Browsers_State_Default"), false),
            BrowserRegistrationStatus.Registered => (Loc.T("Browsers_State_Registered"), false),
            _ => (Loc.T("Browsers_State_NotRegistered"), true),
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
            Services.LogService.Error(ex, "Enregistrement de DockPad comme navigateur (HKCU)");
            AppDialog.Error(Loc.F("Browsers_RegisterError", ex.Message), owner: this);
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
        PnlEdit.IsEnabled = b is not null;

        // Un profil partage l'exe de son navigateur : chemin non modifiable ici.
        bool isProfile = b?.ProfileDirectory is { Length: > 0 };
        TxtExe.IsEnabled = BtnBrowseExe.IsEnabled = !isProfile;
        TxtProfileInfo.Visibility = isProfile ? Visibility.Visible : Visibility.Collapsed;
        if (isProfile)
            TxtProfileInfo.Text = Loc.F("Browsers_ProfileHint", b!.ProfileDirectory, ParentName(b));
    }

    private string ParentName(BrowserEntry child) =>
        _config.Browsers.FirstOrDefault(b => b.Id == child.ParentId)?.Name ?? Loc.T("Browsers_ParentFallback");

    private void Redetect_Click(object sender, RoutedEventArgs e)
    {
        int addedBrowsers = 0;
        foreach (var found in BrowserDetectionService.Detect())
        {
            // Comparaison sur les navigateurs seuls : un profil partage l'exe de son parent.
            if (_config.Browsers.Any(b => b.ParentId is null &&
                    string.Equals(b.ExePath, found.ExePath, StringComparison.OrdinalIgnoreCase)))
                continue;
            found.Order = _config.Browsers.Count == 0 ? 0 : _config.Browsers.Max(b => b.Order) + 1;
            found.IconProfilePath = IconStoreService.CopyToProfile(found.IconPath);
            _config.Browsers.Add(found);
            addedBrowsers++;
        }

        int addedProfiles = 0;
        foreach (var parent in _config.Browsers.Where(b => b.ParentId is null).ToList())
        {
            var profiles = BrowserProfileService.Detect(parent.ExePath);
            foreach (var child in BrowserProfileService.MergeProfiles(_config, parent, profiles))
            {
                child.IconProfilePath = IconStoreService.CopyToProfile(child.IconPath);
                addedProfiles++;
            }
        }

        if (addedBrowsers > 0 || addedProfiles > 0)
        {
            Save();
            RefreshBrowsers();
            RefreshRules();
        }

        // La conjonction (« et » / « and ») et l'accord du participe appartiennent a la langue :
        // le ListFormatter de SmartFormat pose la premiere, le nombre total le second.
        var parts = new List<string>();
        if (addedBrowsers > 0) parts.Add(Loc.F("Browsers_DetectedBrowsers", addedBrowsers));
        if (addedProfiles > 0) parts.Add(Loc.F("Browsers_DetectedProfiles", addedProfiles));
        AppDialog.Info(parts.Count > 0
            ? Loc.F("Browsers_DetectedAdded", addedBrowsers + addedProfiles, parts)
            : Loc.T("Browsers_DetectedNothing"), owner: this);
    }

    private void Up_Click(object sender, RoutedEventArgs e)   => MoveSelected(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        var b = Selected;
        if (b is null) return;

        // Un navigateur emmène ses profils ; un profil se déplace au sein de son groupe.
        BrowserRowLayout.Move(_config, b, delta);
        Save();
        RefreshBrowsers(selectId: b.Id);
    }

    private void VisibleCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox chk || chk.DataContext is not BrowserItem item)
            return;

        item.Entry.Hidden = chk.IsChecked != true;
        Save();
        // Rafraîchit hors du handler : le ListBox recrée ses items, on ne détruit pas
        // le CheckBox pendant son propre événement.
        Dispatcher.BeginInvoke(() => RefreshBrowsers(selectId: item.Entry.Id));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var b = Selected;
        if (b is null) return;

        var children = BrowserRowLayout.Children(_config, b.Id);
        var subject = children.Count > 0
            ? Loc.F("Browsers_SubjectWithProfiles", b.Name, children.Count)
            : Loc.F("Browsers_SubjectAlone", b.Name);
        if (!AppDialog.Confirm(Loc.F("Browsers_ConfirmDelete", subject), owner: this))
            return;

        var ids = children.Select(c => c.Id).Append(b.Id).ToHashSet();
        _config.Browsers.RemoveAll(x => ids.Contains(x.Id));
        _config.Rules.RemoveAll(r => ids.Contains(r.BrowserId));
        BrowserRowLayout.Reindex(_config);
        Save();
        RefreshBrowsers();
        RefreshRules();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // Le nom par defaut est traduit a la creation, puis devient de la donnee utilisateur dans
        // browsers.json : il ne suit pas les bascules de langue ensuite, comme tout nom
        // personnalisable.
        var entry = new BrowserEntry { Name = Loc.T("Browsers_NewBrowser") };
        entry.Order = _config.Browsers.Count == 0 ? 0 : _config.Browsers.Max(b => b.Order) + 1;
        _config.Browsers.Add(entry);
        Save();
        RefreshBrowsers(selectId: entry.Id);
        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("Browsers_Pick_Exe_Filter"), CheckFileExists = true };
        if (dlg.ShowDialog(this) == true) TxtExe.Text = dlg.FileName;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var b = Selected;
        if (b is null) return;

        b.Name      = TxtName.Text.Trim();
        b.Arguments = TxtArgs.Text.Trim();

        if (b.ParentId is null)
        {
            b.ExePath = TxtExe.Text.Trim().Trim('"');
            // Les profils suivent l'exe de leur navigateur.
            foreach (var child in BrowserRowLayout.Children(_config, b.Id)) child.ExePath = b.ExePath;

            // Icône : (ré)extraite de l'exe si aucune icône profil ou si l'exe a changé.
            var cached = IconStoreService.CopyToProfile(b.ExePath);
            if (cached is not null) b.IconProfilePath = cached;
        }

        Save();
        RefreshBrowsers(selectId: b.Id);
        RefreshRules();
    }

    // ── Règles ──────────────────────────────────────────────────────────────────

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleItem item) return;
        _config.Rules.Remove(item.Rule);
        Save();
        RefreshRules();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Icônes (même pattern que BrowserPickerWindow) ───────────────────────────
}
