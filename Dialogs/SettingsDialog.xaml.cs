using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DockPad.Services;
using DockPad.Services.Localization;

namespace DockPad;

/// <summary>Onglet sur lequel ouvrir la fenêtre.</summary>
/// <remarks>
/// La sélection se fait par <b>référence à l'onglet nommé</b>, pas par index : un index se
/// désynchronise en silence dès qu'on réordonne le XAML, et on ouvrirait alors le mauvais onglet
/// sans que rien ne le signale.
/// </remarks>
public enum SettingsTab { General, Integrations, Secrets }

public partial class SettingsDialog : Window
{
    public uint SelectedModifiers { get; private set; }
    public uint SelectedKey { get; private set; }

    private static readonly (string Name, uint VK)[] Keys = HotkeyService.Keys;

    // Index 0 = auto ; les autres valeurs sont stockées telles quelles dans le registre.
    // Une méthode et non un tableau statique : le libellé d'index 0 est traduit, et il doit se
    // reconstruire quand la langue change sous la fenêtre ouverte.
    private static string[] TriggerChoices() =>
        [Loc.T("Settings_Tiles_TriggerAuto"), "Ctrl", "Alt", "Shift"];

    /// <summary>Une entrée de la liste des langues : ce qu'on stocke, et ce qu'on affiche.</summary>
    private sealed record LanguageChoice(string Tag, string Label);
    private sealed record ThemeChoice(string Tag, string Label);

    /// <summary>
    /// Vrai pendant le remplissage des listes : les <c>SelectionChanged</c> qu'il déclenche ne
    /// doivent ni écrire dans le registre ni rebasculer la langue.
    /// </summary>
    private bool _filling;

    public SettingsDialog() : this(SettingsTab.General) { }

    public SettingsDialog(SettingsTab tab)
    {
        InitializeComponent();

        Tabs.SelectedItem = tab switch
        {
            SettingsTab.Integrations => TabIntegrations,
            SettingsTab.Secrets => TabSecrets,
            _ => TabGeneral,
        };

        FillKeys();

        var choices = TriggerChoices();
        CmbTriggerFirst.ItemsSource  = choices;
        CmbTriggerSecond.ItemsSource = choices;
        var (trigFirst, trigSecond) = SettingsService.LoadTriggerMods();
        CmbTriggerFirst.SelectedIndex  = Math.Max(0, Array.IndexOf(choices, trigFirst));
        CmbTriggerSecond.SelectedIndex = Math.Max(0, Array.IndexOf(choices, trigSecond));
        CmbTriggerFirst.SelectionChanged  += (_, _) => ValidateTriggers();
        CmbTriggerSecond.SelectionChanged += (_, _) => ValidateTriggers();

        ChkAutoStart.IsChecked = SettingsService.LoadAutoStart();
        ChkAutoFavicon.IsChecked = SettingsService.LoadAutoFavicon();
        TxtClaudeArgs.Text = SettingsService.LoadClaudeArgs();

        TxtBwPath.Text = SettingsService.LoadBitwardenCliPath();
        TxtVaultOrg.Text = SettingsService.LoadVaultOrganization();
        TxtClearSeconds.Text = SettingsService.LoadClipboardClearSeconds().ToString();
        // L'état vient du registre, pas d'un réglage : c'est la présence des clés qui fait foi, et
        // elle peut avoir changé hors de DockPad.
        ChkInjectMenu.IsChecked = Secrets.SecretMenu.IsInstalled();
        _ = RefreshLastSyncAsync();
        TxtAutoStartPath.Text = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

        TxtVersion.Text = Services.AppInfo.VersionText;

        var (mods, vk) = SettingsService.LoadHotkey();
        ChkCtrl.IsChecked  = (mods & HotkeyService.MOD_CONTROL) != 0;
        ChkAlt.IsChecked   = (mods & HotkeyService.MOD_ALT)     != 0;
        ChkShift.IsChecked = (mods & HotkeyService.MOD_SHIFT)   != 0;
        ChkWin.IsChecked   = (mods & HotkeyService.MOD_WIN)     != 0;

        int idx = Array.FindIndex(Keys, k => k.VK == vk);
        CmbKey.SelectedIndex = idx >= 0 ? idx : 0;

        UpdatePreview();

        ChkCtrl.Checked    += (_, _) => UpdatePreview();
        ChkCtrl.Unchecked  += (_, _) => UpdatePreview();
        ChkAlt.Checked     += (_, _) => UpdatePreview();
        ChkAlt.Unchecked   += (_, _) => UpdatePreview();
        ChkShift.Checked   += (_, _) => UpdatePreview();
        ChkShift.Unchecked += (_, _) => UpdatePreview();
        ChkWin.Checked     += (_, _) => UpdatePreview();
        ChkWin.Unchecked   += (_, _) => UpdatePreview();
        CmbKey.SelectionChanged += (_, _) => UpdatePreview();

        FillLanguages();
        FillThemes();

        // La langue peut changer pendant que cette fenêtre est ouverte — c'est même le cas normal,
        // puisque c'est ici qu'on la change. Les libellés liés par {loc:T} se retraduisent seuls ;
        // ceux construits en code, eux, doivent être refaits.
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        FillLanguages();
        FillThemes();

        // Les noms de touches nommées sont traduits (« Espace » / « Space ») : la liste doit être
        // refaite, la sélection conservée.
        var key = CmbKey.SelectedIndex;
        FillKeys();
        CmbKey.SelectedIndex = key;

        var choices = TriggerChoices();
        int first = CmbTriggerFirst.SelectedIndex, second = CmbTriggerSecond.SelectedIndex;
        _filling = true;
        CmbTriggerFirst.ItemsSource  = choices;
        CmbTriggerSecond.ItemsSource = choices;
        CmbTriggerFirst.SelectedIndex  = first;
        CmbTriggerSecond.SelectedIndex = second;
        _filling = false;

        UpdatePreview();
        ValidateTriggers();
    }

    private void FillKeys()
    {
        _filling = true;
        CmbKey.ItemsSource = Keys.Select(k => HotkeyService.Display(k.Name)).ToList();
        _filling = false;
    }

    /// <summary>
    /// Remplit la liste des langues. Les noms de langue ne sont pas traduits — une langue s'écrit
    /// dans sa propre langue, c'est ce qui permet de la retrouver quand l'interface est dans une
    /// langue qu'on ne lit pas.
    /// </summary>
    private void FillLanguages()
    {
        var selected = SettingsService.LoadLanguage();
        _filling = true;
        CmbLanguage.ItemsSource = new[]
        {
            new LanguageChoice("",   Loc.T("Language_Auto")),
            new LanguageChoice("fr", "Français"),
            new LanguageChoice("en", "English"),
            // Une langue s'écrit dans sa propre langue — celle-ci ne fait pas exception.
            new LanguageChoice("qps-Ploc", "1337"),
        };
        CmbLanguage.DisplayMemberPath = nameof(LanguageChoice.Label);
        CmbLanguage.SelectedIndex = selected switch
        {
            "fr" => 1,
            "en" => 2,
            "qps-Ploc" => 3,
            _ => 0,
        };
        _filling = false;
    }

    /// <summary>
    /// Remplit la liste des thèmes. Les libellés sont traduits, donc refaits à chaque changement
    /// de langue — comme ceux des modificateurs de touches.
    /// </summary>
    private void FillThemes()
    {
        var selected = ThemeService.LoadSetting();
        _filling = true;
        CmbTheme.ItemsSource = new[]
        {
            new ThemeChoice("",      Loc.T("Theme_Auto")),
            new ThemeChoice("Light", Loc.T("Theme_Light")),
            new ThemeChoice("Dark",  Loc.T("Theme_Dark")),
        };
        CmbTheme.DisplayMemberPath = nameof(ThemeChoice.Label);
        CmbTheme.SelectedIndex = selected switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _filling = false;
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || CmbTheme.SelectedItem is not ThemeChoice choice) return;

        // Sauvegarde puis application immédiate, comme la langue : la fenêtre change de couleur
        // sous les yeux, sans attendre le bouton Sauvegarder.
        ThemeService.SaveSetting(choice.Tag);
        ThemeService.Apply(ThemeService.IsDark(choice.Tag, ThemeService.SystemIsDark()));
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || CmbLanguage.SelectedItem is not LanguageChoice choice) return;

        // Sauvegarde immédiate puis application : la fenêtre se retraduit sous les yeux, sans
        // attendre le bouton Sauvegarder — qui ne concerne que le raccourci et le démarrage.
        SettingsService.SaveLanguage(choice.Tag);
        Loc.SetCulture(Loc.Parse(choice.Tag));
    }

    private void UpdatePreview()
    {
        var parts = new List<string>();
        if (ChkCtrl.IsChecked  == true) parts.Add("Ctrl");
        if (ChkAlt.IsChecked   == true) parts.Add("Alt");
        if (ChkShift.IsChecked == true) parts.Add("Shift");
        if (ChkWin.IsChecked   == true) parts.Add("Win");
        // Display et non Name : depuis que Keys porte des identifiants stables, afficher Name
        // donnait « Space » dans un apercu francais sous une liste qui dit « Espace ».
        if (CmbKey.SelectedIndex >= 0)  parts.Add(HotkeyService.Display(Keys[CmbKey.SelectedIndex].Name));

        TxtPreview.Text = parts.Count > 0
            ? Loc.F("Settings_Hotkey_Current", string.Join("+", parts))
            : "";
    }

    // Les deux triggers doivent différer (sauf si l'un des deux est en Auto,
    // auquel cas la paire complète retombe en mode auto).
    private bool ValidateTriggers()
    {
        bool bothExplicit = CmbTriggerFirst.SelectedIndex > 0 && CmbTriggerSecond.SelectedIndex > 0;
        bool conflict = bothExplicit && CmbTriggerFirst.SelectedIndex == CmbTriggerSecond.SelectedIndex;

        TxtTriggerWarn.Text = conflict
            ? Loc.T("Settings_Tiles_WarnSame")
            : (CmbTriggerFirst.SelectedIndex > 0) != (CmbTriggerSecond.SelectedIndex > 0)
                ? Loc.T("Settings_Tiles_WarnPartial")
                : "";
        TxtTriggerWarn.Visibility = TxtTriggerWarn.Text.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;

        return !conflict;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CmbKey.SelectedIndex < 0) return;
        if (!ValidateTriggers()) return;

        uint mods = 0;
        if (ChkCtrl.IsChecked  == true) mods |= HotkeyService.MOD_CONTROL;
        if (ChkAlt.IsChecked   == true) mods |= HotkeyService.MOD_ALT;
        if (ChkShift.IsChecked == true) mods |= HotkeyService.MOD_SHIFT;
        if (ChkWin.IsChecked   == true) mods |= HotkeyService.MOD_WIN;

        SelectedModifiers = mods;
        SelectedKey = Keys[CmbKey.SelectedIndex].VK;

        SettingsService.SaveHotkey(SelectedModifiers, SelectedKey);
        var saved = TriggerChoices();
        SettingsService.SaveTriggerMods(
            CmbTriggerFirst.SelectedIndex  > 0 ? saved[CmbTriggerFirst.SelectedIndex]  : "",
            CmbTriggerSecond.SelectedIndex > 0 ? saved[CmbTriggerSecond.SelectedIndex] : "");
        SettingsService.SaveAutoStart(ChkAutoStart.IsChecked == true);
        SettingsService.SaveAutoFavicon(ChkAutoFavicon.IsChecked == true);
        SettingsService.SaveClaudeArgs(TxtClaudeArgs.Text);

        SettingsService.SaveBitwardenCliPath(TxtBwPath.Text);
        SettingsService.SaveVaultOrganization(TxtVaultOrg.Text);
        // Une saisie illisible retombe sur le défaut plutôt que de désactiver l'effacement en
        // silence : zéro se demande explicitement.
        // Le négatif compte comme illisible : `-1` se parse, et Math.Clamp le ramènerait à 0,
        // soit précisément la désactivation silencieuse que ce repli existe pour empêcher.
        SettingsService.SaveClipboardClearSeconds(
            int.TryParse(TxtClearSeconds.Text.Trim(), out var seconds) && seconds >= 0 ? seconds : 90);

        ApplyContextMenu();

        DialogResult = true;
    }

    /// <summary>
    /// Pose ou retire l'entrée de menu contextuel, selon la case.
    /// </summary>
    /// <remarks>
    /// <b>On réécrit toujours quand la case est cochée</b>, même si les clés existent déjà : c'est
    /// ce qui fait suivre le libellé traduit après un changement de langue, sans avoir à comparer
    /// le nom affiché comme a dû le faire <c>PresetService.CompareStatus</c>.
    /// </remarks>
    private void ApplyContextMenu()
    {
        try
        {
            if (ChkInjectMenu.IsChecked == true) Secrets.SecretMenu.Install();
            else Secrets.SecretMenu.Uninstall();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Entrée de menu contextuel de l'injection de secrets");
            AppDialog.Error(ex.Message, Loc.T("Settings_Inject_Section"));
        }
    }

    /// <summary>
    /// Cherche <c>bw.exe</c> et remplit le champ. Ne remplace rien si la recherche échoue : effacer
    /// une valeur que l'utilisateur a saisie ferait perdre son travail.
    /// </summary>
    private void DetectBw_Click(object sender, RoutedEventArgs e)
    {
        var found = Secrets.BitwardenCli.Locate("");

        if (found is not null) TxtBwPath.Text = found;
        else ButtonFlash.Flash(BtnDetectBw, Loc.T("Settings_Inject_DetectFailed"), TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Ouvre la fenêtre de synchronisation, puis rafraîchit la date affichée.
    /// </summary>
    /// <remarks>
    /// Le mot de passe maître est recueilli par la fenêtre du dossier <c>Secrets/</c>, jamais ici :
    /// la frontière d'audit veut qu'aucun secret ne soit manipulé hors du périmètre.
    /// </remarks>
    private async void SyncVault_Click(object sender, RoutedEventArgs e)
    {
        Secrets.SecretInjection.SyncVault(this);
        await RefreshLastSyncAsync();
    }

    /// <summary>
    /// Affiche l'âge du cache local de la CLI. Ne demande aucun mot de passe.
    /// </summary>
    /// <remarks>
    /// C'est ce chiffre qui désamorce la confusion la plus coûteuse de la fonctionnalité : un item
    /// ajouté au coffre après cette date n'existe pas encore pour DockPad.
    /// </remarks>
    private async Task RefreshLastSyncAsync()
    {
        TxtLastSync.Text = "";

        var when = await Secrets.SecretInjection.LastVaultSyncAsync(CancellationToken.None);

        TxtLastSync.Text = when is null
            ? Loc.T("Settings_Inject_LastSyncUnknown")
            : Loc.F("Settings_Inject_LastSync", when.Value.ToLocalTime());
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
