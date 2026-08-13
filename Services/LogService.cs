using System.IO;
using Serilog;
using Serilog.Core;

namespace DockPad.Services;

/// <summary>
/// Logger central de l'application (Serilog, fichier).
/// %APPDATA%\DockPad\logs\dockpad-YYYYMMDD.log — rolling quotidien, 14 fichiers gardés.
/// shared:true : l'instance relais (clic URL) écrit dans le même fichier que l'instance principale.
/// </summary>
public static class LogService
{
    private static Logger? _logger;

    public static void Init()
    {
        try
        {
            var logDir = Path.Combine(AppPaths.ProfileRoot, "logs");

            _logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDir, "dockpad-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch { /* le logger reste null → no-op */ }
    }

    /// <summary>Erreur montrée à l'utilisateur ou exception non gérée.</summary>
    public static void Error(Exception ex, string context) => _logger?.Error(ex, "{Context}", context);

    /// <summary>Erreur avalée silencieusement (comportement utilisateur inchangé).</summary>
    public static void Warn(Exception ex, string context) => _logger?.Warning(ex, "{Context}", context);

    /// <summary>Événement informatif (actions MCP). Seul le MCP utilise ce niveau.</summary>
    public static void Info(string message) => _logger?.Information("{Message}", message);

    public static void Shutdown() => _logger?.Dispose();
}
