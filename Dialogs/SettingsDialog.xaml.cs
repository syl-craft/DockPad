using System.Windows;
using DockPad.Services;

namespace DockPad;

public partial class SettingsDialog : Window
{
    public uint SelectedModifiers { get; private set; }
    public uint SelectedKey { get; private set; }

    private static readonly (string Name, uint VK)[] Keys = HotkeyService.Keys;

    public SettingsDialog()
    {
        InitializeComponent();

        CmbKey.ItemsSource = Keys.Select(k => k.Name).ToList();

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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CmbKey.SelectedIndex < 0) return;

        uint mods = 0;
        if (ChkCtrl.IsChecked  == true) mods |= HotkeyService.MOD_CONTROL;
        if (ChkAlt.IsChecked   == true) mods |= HotkeyService.MOD_ALT;
        if (ChkShift.IsChecked == true) mods |= HotkeyService.MOD_SHIFT;
        if (ChkWin.IsChecked   == true) mods |= HotkeyService.MOD_WIN;

        SelectedModifiers = mods;
        SelectedKey = Keys[CmbKey.SelectedIndex].VK;

        SettingsService.SaveHotkey(SelectedModifiers, SelectedKey);
        SettingsService.SaveAutoStart(ChkAutoStart.IsChecked == true);
        SettingsService.SaveClaudeArgs(TxtClaudeArgs.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
