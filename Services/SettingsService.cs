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
        if (key is not null)
        {
            var mods = key.GetValue("HotkeyModifiers");
            var vk   = key.GetValue("HotkeyKey");
            if (mods is int m && vk is int k)
                return ((uint)m, (uint)k);
        }

        var def = DefaultHotkey();
        SaveHotkey(def.Item1, def.Item2);
        return def;
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

    public static string LoadClaudeArgs()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue("ClaudeArgs") as string ?? "";
    }

    public static void SaveClaudeArgs(string args)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key.SetValue("ClaudeArgs", args.Trim(), RegistryValueKind.String);
    }

    // Default: Ctrl+Shift+M
    private static (uint, uint) DefaultHotkey() =>
        (HotkeyService.MOD_CONTROL | HotkeyService.MOD_SHIFT, 0x4D);
}
