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
        return JsonConfigFile.Load<BrowsersConfig>(FilePath, JsonOptions);
    }

    public static void Save(BrowsersConfig config)
    {
        config.Browsers = config.Browsers.OrderBy(b => b.Order).ToList();
        JsonConfigFile.Save(FilePath, config, JsonOptions);
    }

    /// <summary>
    /// Détecte et enregistre les navigateurs si la liste est vide et la configuration lisible.
    /// </summary>
    public static BrowsersConfig EnsureInitialized()
    {
        var config = Load();
        if (config.Browsers.Count > 0 || JsonConfigFile.IsReadOnly(FilePath)) return config;

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
