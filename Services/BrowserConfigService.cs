using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

public static class BrowserConfigService
{
    public static readonly string FilePath = AppPaths.File("browsers.json");

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
        catch (Exception ex) { LogService.Warn(ex, "Chargement de browsers.json (config par défaut utilisée)"); return new BrowsersConfig(); }
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
    /// installés, copie leurs icônes dans le store et sauvegarde. Les règles déjà chargées sont conservées.
    /// </summary>
    public static BrowsersConfig EnsureInitialized()
    {
        var config = File.Exists(FilePath) ? Load() : new BrowsersConfig();
        if (config.Browsers.Count > 0) return config;

        config.Browsers = BrowserDetectionService.Detect();
        for (int i = 0; i < config.Browsers.Count; i++)
        {
            config.Browsers[i].Order = i;
            config.Browsers[i].IconProfilePath = IconStoreService.CopyToProfile(config.Browsers[i].IconPath);
        }
        Save(config);
        return config;
    }
}
