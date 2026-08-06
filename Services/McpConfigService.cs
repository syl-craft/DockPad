using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

public static class McpConfigService
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DockPad",
        "mcp.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static McpConfig Load() => Load(FilePath);

    public static McpConfig Load(string path)
    {
        if (!File.Exists(path)) return new McpConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<McpConfig>(json, JsonOptions) ?? new McpConfig();
        }
        catch (Exception ex) { LogService.Warn(ex, "Chargement de mcp.json (config par défaut utilisée)"); return new McpConfig(); }
    }

    public static void Save(McpConfig config) => Save(config, FilePath);

    public static void Save(McpConfig config, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }
}
