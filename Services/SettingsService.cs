using Microsoft.Win32;

namespace WinContextMenuManager.Services;

public static class SettingsService
{
    private const string RegPath = @"Software\WinContextMenuManager\Settings";

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

    // Default: Ctrl+Shift+M
    private static (uint, uint) DefaultHotkey() =>
        (HotkeyService.MOD_CONTROL | HotkeyService.MOD_SHIFT, 0x4D);
}
