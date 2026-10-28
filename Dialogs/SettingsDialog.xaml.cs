using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;

namespace DockPad;

/// <summary>
/// Onglet sur lequel ouvrir la fenêtre.
/// </summary>
/// <remarks>
/// La sélection se fait par <b>référence à l'onglet nommé</b>, pas par index : un index se
/// désynchronise en silence dès qu'on réordonne le XAML, et on ouvrirait alors le mauvais onglet
/// sans que rien ne le signale.
/// </remarks>
public enum SettingsTab { General, Integrations, Secrets, StreamDock }

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
    private sealed record TerminalChoice(string Tag, string Label);

    /// <summary>
    /// Terminaux proposés : ceux de la fenêtre Terminaux, visibles et présents.
    /// </summary>
    private List<Models.TerminalInfo> _terminals = AvailableTerminals();

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
            SettingsTab.StreamDock => TabStreamDock,
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
        ChkStreamDockEnabled.IsChecked = SettingsService.LoadStreamDockEnabled();
        ChkStreamDockSyncPages.IsChecked = SettingsService.LoadStreamDockSyncPages();
        FillSideStripPreviews();
        FillKeyStylePreviews();
        FillBootImage();
        TxtStreamDockDriverMissing.Visibility = File.Exists(Path.Combine(
            SurfaceDriverLoader.PluginsFolder, "StreamDock", "DockPad.StreamDock.dll"))
            ? Visibility.Collapsed : Visibility.Visible;
        TxtClaudeArgs.Text = SettingsService.LoadClaudeArgs();
        FillTerminals(SettingsService.LoadDefaultTerminal());

        TxtBwPath.Text = SettingsService.LoadBitwardenCliPath();
        TxtGhPath.Text = SettingsService.LoadGitHubCliPath();
        TxtVaultOrg.Text = SettingsService.LoadVaultOrganization();
        TxtVaultCollection.Text = SettingsService.LoadVaultCollection();
        TxtClearSeconds.Text = SettingsService.LoadClipboardClearSeconds().ToString();
        // L'état vient du registre, pas d'un réglage : c'est la présence des clés qui fait foi, et
        // elle peut avoir changé hors de DockPad.
        ChkInjectMenu.IsChecked = Secrets.SecretMenu.IsInstalled();
        _ = RefreshLastSyncAsync();
        TxtAutoStartPath.Text = Services.Updates.AppInstallation.Executable;

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

    /// <summary>
    /// Vignettes dessinées par le vrai rendu de la colonne, avec des valeurs d'exemple fixes.
    /// </summary>
    private void FillSideStripPreviews()
    {
        var example = new SideStripContent(
        [
            new QuotaGauge("claude", "Claude", Loc.T("StreamDock_Week"), 68,
                IsAlert: false, IsStale: false, ResetsAt: DateTime.Now.AddDays(2), WindowKind: UsageWindowKind.Week),
            new QuotaGauge("claude", "Claude", Loc.T("StreamDock_Session"), 42,
                IsAlert: false, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Session),
            new QuotaGauge("codex", "Codex", Loc.T("StreamDock_Week"), 12,
                IsAlert: false, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Week),
        ], CpuPct: 23, MemoryPct: 61);

        (SideStripStyle Style, Image Preview)[] previews =
        [
            (SideStripStyle.Columns, ImgSideStripColumns),
            (SideStripStyle.HeroStack, ImgSideStripHeroStack),
            (SideStripStyle.Rings, ImgSideStripRings),
            (SideStripStyle.Reservoir, ImgSideStripReservoir),
        ];
        foreach (var (style, preview) in previews)
        {
            var image = SideStripRenderer.Render(example, style, width: 75, height: 380);
            var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32,
                null, image.Bgra32, image.Width * 4);
            bitmap.Freeze();
            preview.Source = bitmap;
        }

        var saved = SettingsService.LoadStreamDockSideStrip();
        LstStreamDockSideStrip.SelectedItem = LstStreamDockSideStrip.Items.Cast<ListBoxItem>()
            .Where(item => item.Tag is SideStripStyle style && style == saved)
            .FirstOrDefault();

        // Chaque clic montre le rendu sur le dock ; sans Sauvegarder, la fermeture remet celui enregistré.
        LstStreamDockSideStrip.SelectionChanged += (_, _) =>
            (Application.Current as App)?.PreviewStreamDockSideStrip(SelectedSideStripStyle);
        Closed += (_, _) =>
        {
            if (DialogResult != true)
                (Application.Current as App)?.PreviewStreamDockSideStrip(saved);
        };
    }

    /// <summary>
    /// Vignettes des styles : Accueil et les premières tuiles de la grille, par le vrai rendu des touches.
    /// </summary>
    private void FillKeyStylePreviews()
    {
        DrawKeyStylePreviews();
        var saved = SettingsService.LoadStreamDockKeyStyle();
        LstStreamDockKeyStyle.SelectedItem = LstStreamDockKeyStyle.Items.Cast<ListBoxItem>()
            .Where(item => item.Tag is DockKeyStyle style && style == saved)
            .FirstOrDefault();

        // Chaque clic redessine les touches du dock ; sans Sauvegarder, la fermeture remet le style enregistré.
        LstStreamDockKeyStyle.SelectionChanged += (_, _) =>
            (Application.Current as App)?.PreviewStreamDockKeyStyle(SelectedKeyStyle);
        Closed += (_, _) =>
        {
            if (DialogResult != true)
                (Application.Current as App)?.PreviewStreamDockKeyStyle(saved);
        };
    }

    /// <summary>
    /// Redessinées aussi au changement de langue : « Accueil » est écrit dans l'image.
    /// </summary>
    /// <summary>
    /// Image choisie dans la fenêtre, null pour celle de DockPad ; enregistrée avec les autres réglages.
    /// </summary>
    private string? _bootImagePath;

    /// <summary>
    /// Image vérifiée et affichée en aperçu : c'est elle qu'envoie « Appliquer au dock », sans relire le fichier.
    /// </summary>
    private BitmapSource _bootImage = null!;

    /// <summary>
    /// Dernier message, recalculé au changement de langue.
    /// </summary>
    private (Func<string> Message, bool IsError)? _bootImageStatus;

    private void FillBootImage()
    {
        var saved = SettingsService.LoadStreamDockBootImage();
        var image = saved == null ? null : DockBootImage.Read(saved);
        bool isUsable = image != null && DockBootImage.HasBootSize(image);
        ShowBootImage(isUsable ? image! : DockBootImage.Default(), isUsable ? saved : null);
    }

    private void ShowBootImage(BitmapSource image, string? path)
    {
        _bootImage = image;
        _bootImagePath = path;
        ImgStreamDockBootImage.Source = image;
    }

    private void ChooseBootImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("Settings_StreamDock_BootImage_Filter"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        var image = DockBootImage.Read(dialog.FileName);
        if (image == null)
        {
            ShowBootImageStatus(() => Loc.T("Settings_StreamDock_BootImage_Unreadable"), isError: true);
            return;
        }
        if (DockBootImage.HasBootSize(image) == false)
        {
            ShowBootImageStatus(() => Loc.F("Settings_StreamDock_BootImage_WrongSize", image.PixelWidth, image.PixelHeight,
                DockBootImage.Size.Width, DockBootImage.Size.Height), isError: true);
            return;
        }
        ShowBootImage(image, dialog.FileName);
        ShowBootImageStatus(null, isError: false);
    }

    private void DefaultBootImage_Click(object sender, RoutedEventArgs e)
    {
        ShowBootImage(DockBootImage.Default(), path: null);
        ShowBootImageStatus(null, isError: false);
    }

    /// <summary>
    /// Seul envoi de l'image de démarrage : le dock l'écrit dans sa mémoire permanente.
    /// </summary>
    private async void ApplyBootImage_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;
        var image = DockBootImage.ToSurfaceImage(_bootImage);
        BtnStreamDockBootImageApply.IsEnabled = false;
        ShowBootImageStatus(() => Loc.T("Settings_StreamDock_BootImage_Sending"), isError: false);
        try
        {
            bool isSent = await app.ApplyStreamDockBootImageAsync(image);
            ShowBootImageStatus(isSent
                ? () => Loc.T("Settings_StreamDock_BootImage_Sent")
                : () => Loc.T("Settings_StreamDock_BootImage_NoDock"), isError: isSent == false);
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Stream Dock : envoi de l'image de démarrage échoué.");
            ShowBootImageStatus(() => Loc.T("Settings_StreamDock_BootImage_Failed"), isError: true);
        }
        finally
        {
            BtnStreamDockBootImageApply.IsEnabled = true;
        }
    }

    private void ShowBootImageStatus(Func<string>? message, bool isError)
    {
        _bootImageStatus = message == null ? null : (message, isError);
        DrawBootImageStatus();
    }

    private void DrawBootImageStatus()
    {
        var status = _bootImageStatus;
        TxtStreamDockBootImageStatus.Text = status == null ? "" : status.Value.Message();
        TxtStreamDockBootImageStatus.Visibility = status == null ? Visibility.Collapsed : Visibility.Visible;
        TxtStreamDockBootImageStatus.SetResourceReference(TextBlock.ForegroundProperty,
            status?.IsError == true ? "Brush.Danger" : "Brush.TextHint");
    }

    private void DrawKeyStylePreviews()
    {
        var keys = PreviewKeys();
        (DockKeyStyle Style, Image Preview)[] previews =
        [
            (DockKeyStyle.Neon, ImgKeyStyleNeon),
            (DockKeyStyle.Flat, ImgKeyStyleFlat),
            (DockKeyStyle.Classic, ImgKeyStyleClassic),
        ];
        foreach (var (style, preview) in previews)
            preview.Source = KeyRow(keys, style);
    }

    /// <summary>
    /// Les trois premières touches de la première page garnie ; deux tuiles sans icône sur une grille vide.
    /// </summary>
    private static DockKey[] PreviewKeys()
    {
        var window = Application.Current.Windows.OfType<QuickAccessWindow>().FirstOrDefault();
        var entries = window?.ShortcutEntries() ?? [];
        var pagesWithTiles = DockLayout.PagesWithTiles(entries);
        if (window == null || pagesWithTiles.Count == 0)
            return [new DockKey.GoHome(), new DockKey.RunTile(new ShortcutEntry { Name = "DockPad" }),
                new DockKey.RunTile(new ShortcutEntry { Name = "Terminal" })];

        var view = DockLayout.Build(new DockScreen.Page(pagesWithTiles[0], 0), entries, window.ShortcutPages(), keyCount: 15);
        return view.Keys.Take(3).ToArray();
    }

    private static BitmapSource KeyRow(DockKey[] keys, DockKeyStyle style)
    {
        const int size = 96, gap = 8;
        int width = keys.Length * size + (keys.Length - 1) * gap;
        var pixels = new byte[width * size * 4];
        for (int index = 0; index < keys.Length; index++)
        {
            var key = DockImageRenderer.RenderKey(keys[index], style, pressed: false, size);
            for (int y = 0; y < size; y++)
                Buffer.BlockCopy(key.Bgra32, y * size * 4, pixels, (y * width + index * (size + gap)) * 4, size * 4);
        }
        var bitmap = BitmapSource.Create(width, size, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private DockKeyStyle SelectedKeyStyle =>
        LstStreamDockKeyStyle.SelectedItem is ListBoxItem { Tag: DockKeyStyle style }
            ? style : DockKeyStyle.Neon;

    private SideStripStyle SelectedSideStripStyle =>
        LstStreamDockSideStrip.SelectedItem is ListBoxItem { Tag: SideStripStyle style }
            ? style : SideStripStyle.Columns;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        DrawKeyStylePreviews();
        DrawBootImageStatus();
        FillLanguages();
        FillThemes();
        FillTerminals((CmbDefaultTerminal.SelectedItem as TerminalChoice)?.Tag ?? "");

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

    private static List<Models.TerminalInfo> AvailableTerminals() => TerminalCatalog.Available(
        TerminalConfigService.EnsureInitialized().Terminals, TerminalDetectionService.ProgramExists);

    /// <summary>
    /// Remplit la liste des terminaux : Automatique, puis ceux de la fenêtre Terminaux. Un terminal
    /// réglé mais masqué ou absent reste dans la liste, sans quoi Sauvegarder effacerait le réglage.
    /// </summary>
    private void FillTerminals(string selected)
    {
        var choices = new List<TerminalChoice> { new("", Loc.T("Settings_Terminal_Auto")) };
        choices.AddRange(_terminals.Select(t => new TerminalChoice(t.Id, t.DisplayName)));
        if (choices.All(c => c.Tag != selected))
            choices.Add(new TerminalChoice(selected, Loc.F("Settings_Terminal_Missing", selected)));

        CmbDefaultTerminal.ItemsSource = choices;
        CmbDefaultTerminal.DisplayMemberPath = nameof(TerminalChoice.Label);
        CmbDefaultTerminal.SelectedItem = choices.Where(c => c.Tag == selected).First();
    }

    private string SelectedTerminalTag => (CmbDefaultTerminal.SelectedItem as TerminalChoice)?.Tag ?? "";

    private Models.TerminalInfo? ChosenTerminal() => TerminalDetectionService.Resolve(SelectedTerminalTag, _terminals);

    private void DefaultTerminal_Changed(object sender, SelectionChangedEventArgs e) => RefreshTerminalPreview();

    /// <summary>
    /// La commande finale du prédéfini Claude, variables remplacées.
    /// </summary>
    private void RefreshTerminalPreview()
    {
        if (TxtTerminalPreview == null || TxtClaudeArgs == null) return;

        var terminal = ChosenTerminal();
        bool runsCommand = terminal == null
            || terminal.CommandArgs.Contains(TerminalDetectionService.CommandToken)
            || terminal.CommandArgs.Contains(TerminalDetectionService.PowerShellCommandToken);

        TxtTerminalNoCommand.Visibility = runsCommand ? Visibility.Collapsed : Visibility.Visible;
        BtnTestTerminal.IsEnabled = terminal != null;
        TxtTerminalPreview.Text = terminal == null
            ? Loc.T("Quick_NoTerminal")
            : PresetService.PreviewCommand(terminal, PresetService.ClaudeRunCommand(TxtClaudeArgs.Text));
    }

    private void ClaudeArgs_Changed(object sender, TextChangedEventArgs e) => RefreshTerminalPreview();

    /// <summary>
    /// Ouvre la fenêtre Terminaux, puis relit la liste : son « Utiliser par défaut » a pu changer le choix.
    /// </summary>
    private void ManageTerminals_Click(object sender, RoutedEventArgs e)
    {
        string before = SettingsService.LoadDefaultTerminal();
        new TerminalConfigDialog { Owner = this }.ShowDialog();

        string after = SettingsService.LoadDefaultTerminal();
        _terminals = AvailableTerminals();
        FillTerminals(after != before ? after : SelectedTerminalTag);
    }

    private void TestTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (ChosenTerminal() is not { } terminal) return;
        TerminalConfigDialog.Test(terminal, PresetService.ClaudeRunCommand(TxtClaudeArgs.Text), owner: this);
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
        SettingsService.SaveDefaultTerminal(SelectedTerminalTag);

        SettingsService.SaveBitwardenCliPath(TxtBwPath.Text);
        SettingsService.SaveGitHubCliPath(TxtGhPath.Text);
        SettingsService.SaveVaultOrganization(TxtVaultOrg.Text);
        SettingsService.SaveVaultCollection(TxtVaultCollection.Text);
        // Une saisie illisible retombe sur le défaut plutôt que de désactiver l'effacement en
        // silence : zéro se demande explicitement.
        // Le négatif compte comme illisible : `-1` se parse, et Math.Clamp le ramènerait à 0,
        // soit précisément la désactivation silencieuse que ce repli existe pour empêcher.
        SettingsService.SaveClipboardClearSeconds(
            int.TryParse(TxtClearSeconds.Text.Trim(), out var seconds) && seconds >= 0 ? seconds : 90);

        ApplyContextMenu();

        SettingsService.SaveStreamDockEnabled(ChkStreamDockEnabled.IsChecked == true);
        SettingsService.SaveStreamDockSyncPages(ChkStreamDockSyncPages.IsChecked == true);
        SettingsService.SaveStreamDockSideStrip(SelectedSideStripStyle);
        SettingsService.SaveStreamDockKeyStyle(SelectedKeyStyle);
        SettingsService.SaveStreamDockBootImage(_bootImagePath);
        (Application.Current as App)?.ApplyStreamDockSettings();

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

    /// <summary>Cherche <c>gh.exe</c> et remplit le champ — même règle : un échec n'efface rien.</summary>
    private void DetectGh_Click(object sender, RoutedEventArgs e)
    {
        var found = Secrets.GitHubCli.Locate("");

        if (found != null) TxtGhPath.Text = found;
        else ButtonFlash.Flash(BtnDetectGh, Loc.T("Settings_Inject_GhDetectFailed"), TimeSpan.FromSeconds(2));
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
