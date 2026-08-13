using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

public static class ShortcutService
{
    public static readonly string FilePath = AppPaths.File("shortcuts.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static List<ShortcutEntry> Load()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ShortcutEntry>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) { LogService.Warn(ex, "Chargement de shortcuts.json (liste vide utilisée)"); return []; }
    }

    public static void Save(List<ShortcutEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    public static void OpenInEditor()
    {
        EnsureFileExists();
        Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
    }

    private static void EnsureFileExists()
    {
        if (File.Exists(FilePath)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Crée un fichier d'exemple avec quelques entrées
        var defaults = new List<ShortcutEntry>
        {
            new() { Row = 0, Col = 0, Name = "Explorateur", Command = "explorer.exe",
                    IconPath = @"C:\Windows\explorer.exe" },
            new() { Row = 0, Col = 1, Name = "Bloc-notes",  Command = "notepad.exe",
                    IconPath = @"C:\Windows\System32\notepad.exe" },
            new() { Row = 0, Col = 2, Name = "Calculatrice", Command = "calc.exe",
                    IconPath = @"C:\Windows\System32\calc.exe" },
        };

        File.WriteAllText(FilePath, JsonSerializer.Serialize(defaults, JsonOptions));
    }
}
