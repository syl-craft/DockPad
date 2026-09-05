using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

public static class PageConfigService
{
    public static readonly string FilePath = AppPaths.File("pages.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static List<PageConfig> Load() => Load(FilePath);

    /// <summary>Les pages d'un fichier donné — celles des raccourcis, ou celles des favoris.</summary>
    public static List<PageConfig> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<PageConfig>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) { LogService.Warn(ex, $"Chargement de {Path.GetFileName(path)} (liste vide utilisée)"); return []; }
    }

    public static void Save(List<PageConfig> pages) => Save(pages, FilePath);

    public static void Save(List<PageConfig> pages, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(pages, JsonOptions));
    }
}
