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

    public static List<ShortcutEntry> Load() => Load(FilePath);

    /// <summary>
    /// Les tuiles d'un fichier donné — les raccourcis, ou les favoris.
    /// </summary>
    /// <remarks>
    /// Les deux grilles ont le <b>même format</b> : c'est ce qui permet à un seul lecteur de les
    /// servir toutes les deux, et à <c>favorites.json</c> d'être sauvegardé et édité à la main
    /// exactement comme <c>shortcuts.json</c>.
    /// </remarks>
    public static List<ShortcutEntry> Load(string path)
    {
        return JsonConfigFile.Load<List<ShortcutEntry>>(path, JsonOptions);
    }

    public static void Save(List<ShortcutEntry> entries) => Save(entries, FilePath);

    public static void Save(List<ShortcutEntry> entries, string path)
    {
        JsonConfigFile.Save(path, entries, JsonOptions);
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

        Save(defaults);
    }
}
