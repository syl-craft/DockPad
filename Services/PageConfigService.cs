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

    public static List<PageConfig> Load()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<PageConfig>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) { LogService.Warn(ex, "Chargement de pages.json (liste vide utilisée)"); return []; }
    }

    public static void Save(List<PageConfig> pages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(pages, JsonOptions));
    }
}
