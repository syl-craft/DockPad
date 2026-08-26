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

    /// <summary>
    /// Modificateurs des raccourcis de tuiles (overlay) : côté gauche / côté droit.
    /// "" = auto (déduits du raccourci global), sinon "Ctrl" | "Alt" | "Shift".
    /// </summary>
    public static (string First, string Second) LoadTriggerMods()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return (key?.GetValue("TriggerFirst")  as string ?? "",
                key?.GetValue("TriggerSecond") as string ?? "");
    }

    public static void SaveTriggerMods(string first, string second)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key.SetValue("TriggerFirst",  first,  RegistryValueKind.String);
        key.SetValue("TriggerSecond", second, RegistryValueKind.String);
    }

    /// <summary>
    /// Étiquette de langue choisie, ou chaîne vide pour « automatique ». Même convention que
    /// <c>TriggerFirst</c>/<c>TriggerSecond</c> : le vide veut dire « laisse le système décider ».
    /// </summary>
    public static string LoadLanguage()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue("Language") as string ?? "";
    }

    public static void SaveLanguage(string tag)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key.SetValue("Language", tag, RegistryValueKind.String);
    }

    /// <summary>
    /// Télécharger l'icône du site pour les tuiles web. Absent du registre = activé : c'est le
    /// comportement demandé, et un réglage réseau qu'on n'a jamais vu ne peut pas avoir été refusé.
    /// </summary>
    public static bool LoadAutoFavicon()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue("AutoFavicon") is not int v || v != 0;
    }

    public static void SaveAutoFavicon(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key.SetValue("AutoFavicon", enabled ? 1 : 0, RegistryValueKind.DWord);
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
