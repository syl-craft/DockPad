using System.Collections.Generic;
using Microsoft.Win32;
using WinContextMenuManager.Models;

namespace WinContextMenuManager.Services;

public static class RegistryService
{
    public static List<ContextMenuEntry> LoadAll()
    {
        var entries = new List<ContextMenuEntry>();
        foreach (ContextMenuTarget target in Enum.GetValues<ContextMenuTarget>())
            entries.AddRange(LoadForTarget(target));
        return entries;
    }

    public static List<ContextMenuEntry> LoadForTarget(ContextMenuTarget target)
    {
        var entries = new List<ContextMenuEntry>();
        string path = ContextMenuEntry.GetRegistryPath(target);

        using var key = Registry.ClassesRoot.OpenSubKey(path, writable: false);
        if (key == null) return entries;

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName, writable: false);
            if (subKey == null) continue;

            // Skip entries disabled via LegacyDisable
            if (subKey.GetValue("LegacyDisable") != null) continue;

            string raw = subKey.GetValue(null)?.ToString() ?? subKeyName;
            string displayName = ResourceStringResolver.Resolve(raw).Replace("&", "");
            string icon = subKey.GetValue("Icon")?.ToString() ?? "";

            string command = "";
            using var cmdKey = subKey.OpenSubKey("command", writable: false);
            if (cmdKey != null)
                command = cmdKey.GetValue(null)?.ToString() ?? "";

            entries.Add(new ContextMenuEntry
            {
                RegistryKey = subKeyName,
                DisplayName = displayName,
                Command = command,
                IconPath = icon,
                Target = target
            });
        }

        return entries;
    }

    public static void Save(ContextMenuEntry entry)
    {
        string basePath = ContextMenuEntry.GetRegistryPath(entry.Target);
        string entryPath = $@"{basePath}\{entry.RegistryKey}";

        using var key = Registry.ClassesRoot.CreateSubKey(entryPath, writable: true)
            ?? throw new InvalidOperationException($"Impossible de créer la clé : {entryPath}");

        // Remove LegacyDisable if re-enabling a previously disabled entry
        key.DeleteValue("LegacyDisable", throwOnMissingValue: false);

        key.SetValue(null, entry.DisplayName);

        if (!string.IsNullOrWhiteSpace(entry.IconPath))
            key.SetValue("Icon", entry.IconPath);
        else
            key.DeleteValue("Icon", throwOnMissingValue: false);

        using var cmdKey = key.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException("Impossible de créer la sous-clé command");

        cmdKey.SetValue(null, entry.Command);
    }

    public static void Delete(ContextMenuEntry entry)
    {
        string basePath = ContextMenuEntry.GetRegistryPath(entry.Target);
        string entryPath = $@"{basePath}\{entry.RegistryKey}";

        // Try deleting from HKLM first (system entries)
        using var hklmKey = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Classes\{entryPath}", writable: true);

        if (hklmKey != null)
        {
            // Entry lives in HKLM — can't safely delete it.
            // Use LegacyDisable in HKCU to suppress it from the context menu.
            using var hkcuKey = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Classes\{entryPath}", writable: true);
            hkcuKey?.SetValue("LegacyDisable", "", RegistryValueKind.String);
        }
        else
        {
            // User-created entry in HKCU — delete it entirely
            Registry.ClassesRoot.DeleteSubKeyTree(entryPath, throwOnMissingSubKey: false);
        }
    }

    public static bool KeyExists(ContextMenuTarget target, string registryKey)
    {
        string basePath = ContextMenuEntry.GetRegistryPath(target);
        using var key = Registry.ClassesRoot.OpenSubKey($@"{basePath}\{registryKey}", writable: false);
        if (key == null) return false;
        return key.GetValue("LegacyDisable") == null;
    }

    /// <summary>Returns the stored command and icon for a key, or null if not found/disabled.</summary>
    public static (string Command, string Icon)? GetValues(ContextMenuTarget target, string registryKey)
    {
        string basePath = ContextMenuEntry.GetRegistryPath(target);
        using var key = Registry.ClassesRoot.OpenSubKey($@"{basePath}\{registryKey}", writable: false);
        if (key == null || key.GetValue("LegacyDisable") != null) return null;

        string icon = key.GetValue("Icon")?.ToString() ?? "";
        using var cmdKey = key.OpenSubKey("command", writable: false);
        string command = cmdKey?.GetValue(null)?.ToString() ?? "";
        return (command, icon);
    }
}
