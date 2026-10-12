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
    /// vide et le fichier lisible. La reprise faite, ces réglages sont vidés, sauf les arguments d'un
    /// terminal absent ce jour-là, que la prochaine redétection reprendra.
    /// </summary>
    public static TerminalsConfig EnsureInitialized()
    {
        var config = Load();
        if (config.Terminals.Count > 0 || JsonConfigFile.IsReadOnly(FilePath)) return config;

        var migration = TerminalCatalog.Migrate(TerminalDetectionService.Detect(), AppSettingsService.Current);
        config.Terminals = migration.Terminals;
        Save(config);

        AppSettingsService.Update(s =>
        {
            s.DefaultTerminal   = migration.DefaultTerminal;
            s.TerminalTemplates = migration.PendingTemplates;
            s.Terminals         = new();
            s.CustomTerminal    = new();
        });
        return config;
    }

    /// <summary>
    /// Ajoute à la liste les terminaux détectés qui n'y sont pas, et rend leur nombre ; un terminal
    /// qui revient reprend les arguments que <c>settings.json</c> gardait pour lui.
    /// </summary>
    public static int MergeDetected(TerminalsConfig config)
    {
        var pending = new Dictionary<string, TerminalSettings>(AppSettingsService.Current.TerminalTemplates);
        int pendingBefore = pending.Count;

        int added = TerminalCatalog.Merge(config.Terminals, TerminalDetectionService.Detect(), pending);
        Save(config);

        if (pending.Count != pendingBefore)
            AppSettingsService.Update(s => s.TerminalTemplates = pending);
        return added;
    }
}
