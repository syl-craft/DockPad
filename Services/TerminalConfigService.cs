using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Load/Save de <c>terminals.json</c> (%APPDATA%\DockPad\terminals.json).
/// </summary>
public static class TerminalConfigService
{
    public static readonly string FilePath = AppPaths.File("terminals.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static TerminalsConfig Load() => JsonConfigFile.Load<TerminalsConfig>(FilePath, JsonOptions);

    public static void Save(TerminalsConfig config)
    {
        config.Terminals = config.Terminals.OrderBy(t => t.Order).ToList();
        JsonConfigFile.Save(FilePath, config, JsonOptions);
    }

    /// <summary>
    /// La liste, créée à partir de la détection et des réglages de <c>settings.json</c> si elle est
    /// vide et le fichier lisible ; la reprise faite, ces réglages sont vidés.
    /// </summary>
    public static TerminalsConfig EnsureInitialized()
    {
        var config = Load();
        if (config.Terminals.Count > 0 || JsonConfigFile.IsReadOnly(FilePath)) return config;

        var settings = AppSettingsService.Current;
        config.Terminals = TerminalCatalog.Initial(TerminalDetectionService.Detect(), settings.TerminalTemplates, settings.Terminals);
        Save(config);

        if (settings.TerminalTemplates.Count > 0 || settings.Terminals.Count > 0)
            AppSettingsService.Update(s => { s.TerminalTemplates = new(); s.Terminals = new(); });
        return config;
    }
}
