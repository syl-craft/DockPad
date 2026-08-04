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
}
