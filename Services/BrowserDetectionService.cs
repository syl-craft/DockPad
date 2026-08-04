using System.IO;
using DockPad.Models;
using Microsoft.Win32;

namespace DockPad.Services;

public static class BrowserDetectionService
{
    private const string ClientsPath = @"Software\Clients\StartMenuInternet";

    /// <summary>
    /// Détecte les navigateurs installés via Software\Clients\StartMenuInternet (HKLM puis HKCU).
    /// DockPad est exclu, les doublons (même exe dans les deux ruches) aussi.
    /// </summary>
    public static List<BrowserEntry> Detect()
    {
        var result = new List<BrowserEntry>();

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var clients = root.OpenSubKey(ClientsPath);
            if (clients is null) continue;

            foreach (var subName in clients.GetSubKeyNames())
            {
                using var sub = clients.OpenSubKey(subName);
                if (sub is null) continue;

                var name = sub.GetValue(null) as string ?? subName;
                if (name.Contains("DockPad", StringComparison.OrdinalIgnoreCase)) continue;

                using var cmdKey = sub.OpenSubKey(@"shell\open\command");
                var exePath = ExtractExePath(cmdKey?.GetValue(null) as string);
                if (exePath is null || !File.Exists(exePath)) continue;

                if (result.Any(b => string.Equals(b.ExePath, exePath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                result.Add(new BrowserEntry { Name = name, ExePath = exePath, IconPath = exePath });
            }
        }

        return result;
    }

    /// <summary>Extrait le chemin d'exe d'une commande registre : "C:\...\x.exe" args | C:\...\x.exe args.</summary>
    public static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}
