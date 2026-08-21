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

            // Les doublons d'id sont écartés ici, à la seule porte d'entrée du fichier. Plus loin,
            // la fusion et l'agrégation indexent cette liste par id : un doublon y lèverait, et
            // l'exception, attrapée plus haut, masquerait le bandeau entier — exactement ce que la
            // tolérance ci-dessus cherche à éviter. La première entrée gagne.
            config.Providers = config.Providers
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

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
