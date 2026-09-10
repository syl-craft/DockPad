using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

public static class McpConfigService
{
    public static readonly string FilePath = AppPaths.File("mcp.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static McpConfig Load() => Load(FilePath);

    public static McpConfig Load(string path)
    {
        return JsonConfigFile.Load<McpConfig>(path, JsonOptions);
    }

    public static void Save(McpConfig config) => Save(config, FilePath);

    public static void Save(McpConfig config, string path)
    {
        JsonConfigFile.Save(path, config, JsonOptions);
    }
}
