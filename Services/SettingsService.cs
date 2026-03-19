using Microsoft.Win32;

namespace DockPad.Services;

public static class SettingsService
{
    private const string RegPath    = @"Software\DockPad\Settings";
    private const string RunPath    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName    = "DockPad";

    public static (uint Modifiers, uint Key) LoadHotkey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        if (key is null) return DefaultHotkey();

        var mods = key.GetValue("HotkeyModifiers");
        var vk   = key.GetValue("HotkeyKey");

        return mods is int m && vk is int k
            ? ((uint)m, (uint)k)
            : DefaultHotkey();
    }

    public static void SaveHotkey(uint modifiers, uint key)
    {
        using var regKey = Registry.CurrentUser.CreateSubKey(RegPath);
        regKey.SetValue("HotkeyModifiers", (int)modifiers, RegistryValueKind.DWord);
        regKey.SetValue("HotkeyKey",       (int)key,       RegistryValueKind.DWord);
    }

    public static bool LoadAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(AppName) is not null;
    }

    public static void SaveAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            key.SetValue(AppName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    // Default: Ctrl+Shift+M
    private static (uint, uint) DefaultHotkey() =>
        (HotkeyService.MOD_CONTROL | HotkeyService.MOD_SHIFT, 0x4D);
}
