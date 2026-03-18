using System.Windows;
using System.Windows.Interop;
using WinContextMenuManager.Services;

namespace WinContextMenuManager;

public partial class DashboardWindow : Window
{
    private IntPtr _hwnd;
    private QuickAccessWindow? _hotkeyWindow;

    public DashboardWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        RegisterHotkey();
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterHotkey();
        base.OnClosed(e);
    }

    private void RegisterHotkey()
    {
        var (mods, key) = SettingsService.LoadHotkey();
        HotkeyService.RegisterHotKey(_hwnd, HotkeyService.HotkeyId, mods | HotkeyService.MOD_NOREPEAT, key);
    }

    private void UnregisterHotkey()
    {
        HotkeyService.UnregisterHotKey(_hwnd, HotkeyService.HotkeyId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyService.WM_HOTKEY && wParam.ToInt32() == HotkeyService.HotkeyId)
        {
            OpenHotkeyWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OpenHotkeyWindow()
    {
        if (_hotkeyWindow is { IsLoaded: true })
        {
            _hotkeyWindow.WindowState = WindowState.Normal;
            _hotkeyWindow.Activate();
            return;
        }

        _hotkeyWindow = new QuickAccessWindow();
        _hotkeyWindow.Show();
    }

    private void OpenContextMenuManager_Click(object sender, RoutedEventArgs e)
    {
        var win = new ContextMenuManagerWindow();
        win.Show();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        UnregisterHotkey();
        RegisterHotkey();
    }
}
