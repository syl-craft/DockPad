using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>Load/Save de usage.json (%APPDATA%\DockPad\usage.json).</summary>
public static class UsageConfigService
{
    public static readonly string FilePath = AppPaths.File("usage.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static UsageConfig Load() => Load(FilePath);

    public static UsageConfig Load(string path)
    {
        if (!File.Exists(path)) return new UsageConfig();
        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<UsageConfig>(json, JsonOptions) ?? new UsageConfig();
            // Une entrée sans id n'a pas de clé de fusion : inexploitable, mais elle ne doit pas
            // emporter le reste du fichier avec elle.
            config.Providers.RemoveAll(p => string.IsNullOrWhiteSpace(p.Id));
            return config;
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Chargement de usage.json (config par défaut utilisée)");
            return new UsageConfig();
        }
    }

    public static void Save(UsageConfig config) => Save(config, FilePath);

    public static void Save(UsageConfig config, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
