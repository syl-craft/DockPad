using System.Windows;
using DockPad.Services;

namespace DockPad;

public partial class SettingsDialog : Window
{
    public uint SelectedModifiers { get; private set; }
    public uint SelectedKey { get; private set; }

    private static readonly (string Name, uint VK)[] Keys = HotkeyService.Keys;

    // Index 0 = auto ; les autres valeurs sont stockées telles quelles dans le registre
    private static readonly string[] TriggerChoices =
        ["Auto (selon le raccourci global)", "Ctrl", "Alt", "Shift"];

    public SettingsDialog()
    {
        InitializeComponent();

        CmbKey.ItemsSource = Keys.Select(k => k.Name).ToList();

        CmbTriggerFirst.ItemsSource  = TriggerChoices;
        CmbTriggerSecond.ItemsSource = TriggerChoices;
        var (trigFirst, trigSecond) = SettingsService.LoadTriggerMods();
        CmbTriggerFirst.SelectedIndex  = Math.Max(0, Array.IndexOf(TriggerChoices, trigFirst));
        CmbTriggerSecond.SelectedIndex = Math.Max(0, Array.IndexOf(TriggerChoices, trigSecond));
        CmbTriggerFirst.SelectionChanged  += (_, _) => ValidateTriggers();
        CmbTriggerSecond.SelectionChanged += (_, _) => ValidateTriggers();

        ChkAutoStart.IsChecked = SettingsService.LoadAutoStart();
        TxtClaudeArgs.Text = SettingsService.LoadClaudeArgs();
        TxtAutoStartPath.Text = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        TxtVersion.Text = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "";

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
    }

    private void UpdatePreview()
    {
        var parts = new List<string>();
        if (ChkCtrl.IsChecked  == true) parts.Add("Ctrl");
        if (ChkAlt.IsChecked   == true) parts.Add("Alt");
        if (ChkShift.IsChecked == true) parts.Add("Shift");
        if (ChkWin.IsChecked   == true) parts.Add("Win");
        if (CmbKey.SelectedIndex >= 0)  parts.Add(Keys[CmbKey.SelectedIndex].Name);

        TxtPreview.Text = parts.Count > 0
            ? $"Raccourci actuel : {string.Join("+", parts)}"
            : "";
    }

    // Les deux triggers doivent différer (sauf si l'un des deux est en Auto,
    // auquel cas la paire complète retombe en mode auto).
    private bool ValidateTriggers()
    {
        bool bothExplicit = CmbTriggerFirst.SelectedIndex > 0 && CmbTriggerSecond.SelectedIndex > 0;
        bool conflict = bothExplicit && CmbTriggerFirst.SelectedIndex == CmbTriggerSecond.SelectedIndex;

        TxtTriggerWarn.Text = conflict
            ? "Les deux moitiés doivent utiliser des modificateurs différents."
            : (CmbTriggerFirst.SelectedIndex > 0) != (CmbTriggerSecond.SelectedIndex > 0)
                ? "Les deux moitiés doivent être configurées ensemble — sinon le mode Auto s'applique."
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
        SettingsService.SaveTriggerMods(
            CmbTriggerFirst.SelectedIndex  > 0 ? TriggerChoices[CmbTriggerFirst.SelectedIndex]  : "",
            CmbTriggerSecond.SelectedIndex > 0 ? TriggerChoices[CmbTriggerSecond.SelectedIndex] : "");
        SettingsService.SaveAutoStart(ChkAutoStart.IsChecked == true);
        SettingsService.SaveClaudeArgs(TxtClaudeArgs.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
