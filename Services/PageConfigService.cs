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
        return JsonConfigFile.Load<List<PageConfig>>(path, JsonOptions);
    }

    public static void Save(List<PageConfig> pages) => Save(pages, FilePath);

    public static void Save(List<PageConfig> pages, string path)
    {
        JsonConfigFile.Save(path, pages, JsonOptions);
    }
}
