using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

public static class BrowserConfigService
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DockPad",
        "browsers.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static BrowsersConfig Load()
    {
        if (!File.Exists(FilePath)) return new BrowsersConfig();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<BrowsersConfig>(json, JsonOptions) ?? new BrowsersConfig();
        }
        catch { return new BrowsersConfig(); }
    }

    public static void Save(BrowsersConfig config)
    {
        config.Browsers = config.Browsers.OrderBy(b => b.Order).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(config, JsonOptions));
    }

    /// <summary>
    /// Charge la config ; si browsers.json n'existe pas encore, est vide ou corrompu
    /// (Load() retombe alors sur une liste de navigateurs vide), détecte les navigateurs
    /// installés, cache leurs icônes et sauvegarde. Les règles déjà chargées sont conservées.
    /// </summary>
    public static BrowsersConfig EnsureInitialized()
    {
        var config = File.Exists(FilePath) ? Load() : new BrowsersConfig();
        if (config.Browsers.Count > 0) return config;

        config.Browsers = BrowserDetectionService.Detect();
        for (int i = 0; i < config.Browsers.Count; i++)
        {
            config.Browsers[i].Order = i;
            config.Browsers[i].IconProfilePath = IconCacheService.CopyToProfile(config.Browsers[i].ExePath);
        }
        Save(config);
        return config;
    }
}
