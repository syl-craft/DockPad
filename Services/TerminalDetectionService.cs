using System.Diagnostics;
using System.IO;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Le terminal par défaut des Options, lancé par les prédéfinis et par toutes les tuiles Terminal.
/// </summary>
public static class TerminalDetectionService
{
    public const string WindowsTerminalId   = "wt";
    public const string PowerShell7Id       = "pwsh";
    public const string WindowsPowerShellId = "powershell";

    /// <summary>
    /// Valeur du réglage quand le terminal est désigné par un chemin choisi dans les Options.
    /// </summary>
    public const string CustomId = "custom";

    /// <summary>
    /// Terminaux installés, dans l'ordre du mode Automatique.
    /// </summary>
    public static List<TerminalInfo> Detect()
    {
        var list = new List<TerminalInfo>();

        // Windows Terminal (AppX package)
        var wtPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(wtPath))
            list.Add(new TerminalInfo { Id = WindowsTerminalId, DisplayName = "Windows Terminal", ExePath = wtPath });

        // PowerShell 7
        var pwshPath = FirstExisting(
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe") ?? WhereExe("pwsh.exe");
        if (pwshPath != null)
            list.Add(new TerminalInfo { Id = PowerShell7Id, DisplayName = "PowerShell 7 (pwsh)", ExePath = pwshPath });

        // Windows PowerShell
        var poshPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(poshPath))
            list.Add(new TerminalInfo { Id = WindowsPowerShellId, DisplayName = "Windows PowerShell", ExePath = poshPath });

        return list;
    }

    /// <summary>
    /// Le terminal choisi s'il est disponible, sinon le premier installé : vide, inconnu, désinstallé
    /// ou chemin personnalisé invalide (<paramref name="custom"/> nul) valent Automatique.
    /// </summary>
    public static TerminalInfo? Resolve(string setting, TerminalInfo? custom, IReadOnlyList<TerminalInfo> installed)
    {
        var chosen = setting == CustomId
            ? custom
            : installed.Where(t => t.Id == setting).FirstOrDefault();

        return chosen ?? installed.FirstOrDefault();
    }

    /// <summary>
    /// Le terminal désigné par un chemin, reconnu au nom de l'exécutable ; <c>null</c> pour tout
    /// autre programme, auquel on ne saurait passer ni le dossier ni la commande.
    /// </summary>
    public static TerminalInfo? CustomTerminal(string path)
    {
        string? id = Path.GetFileName(path).ToLowerInvariant() switch
        {
            "wt.exe"         => WindowsTerminalId,
            "pwsh.exe"       => PowerShell7Id,
            "powershell.exe" => WindowsPowerShellId,
            _                => null,
        };

        return id == null ? null : new TerminalInfo { Id = id, DisplayName = path, ExePath = path };
    }

    /// <summary>
    /// Le chemin personnalisé s'il existe et désigne un terminal reconnu, sinon <c>null</c>.
    /// </summary>
    public static TerminalInfo? ExistingCustomTerminal(string path) =>
        File.Exists(path) ? CustomTerminal(path) : null;

    /// <summary>
    /// Le terminal à lancer selon les Options, ou <c>null</c> si aucun n'est installé.
    /// </summary>
    public static TerminalInfo? ResolveDefault() =>
        Resolve(SettingsService.LoadDefaultTerminal(),
                ExistingCustomTerminal(SettingsService.LoadCustomTerminalPath()),
                Detect());

    /// <summary>
    /// Arguments qui ouvrent <paramref name="terminal"/> dans le dossier de la config et y lancent sa commande.
    /// </summary>
    public static string BuildArgs(TerminalInfo terminal, TerminalConfig cfg) => terminal.Id switch
    {
        WindowsTerminalId => BuildWtArgs(cfg),
        _                 => BuildPowerShellArgs(cfg),
    };

    private static string BuildWtArgs(TerminalConfig cfg)
    {
        var parts = new List<string>();
        if (cfg.NewTab) parts.Add("-w 0 new-tab");
        if (!string.IsNullOrWhiteSpace(cfg.StartingDirectory))
            parts.Add($"--startingDirectory \"{cfg.StartingDirectory}\"");
        if (!string.IsNullOrWhiteSpace(cfg.ExtraArgs))
            parts.Add(cfg.ExtraArgs.Trim());
        if (!string.IsNullOrWhiteSpace(cfg.RunCommand))
            parts.Add($"-- {cfg.RunCommand.Trim()}");
        return string.Join(" ", parts);
    }

    private static string BuildPowerShellArgs(TerminalConfig cfg)
    {
        var cmds = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.StartingDirectory))
            cmds.Add($"Set-Location '{cfg.StartingDirectory}'");
        if (!string.IsNullOrWhiteSpace(cfg.RunCommand))
            cmds.Add(PowerShellCall(cfg.RunCommand.Trim()));

        var parts = new List<string> { "-NoExit" };
        if (cmds.Count > 0)
        {
            // Échappés, les guillemets de la commande ne ferment pas ceux de -Command.
            string script = string.Join("; ", cmds).Replace("\"", "\\\"");
            parts.Add($"-Command \"{script}\"");
        }
        if (!string.IsNullOrWhiteSpace(cfg.ExtraArgs))
            parts.Add(cfg.ExtraArgs.Trim());
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Préfixe d'un <c>&amp;</c> une commande qui commence par un chemin entre guillemets, que
    /// PowerShell afficherait sinon comme une chaîne au lieu de l'exécuter.
    /// </summary>
    private static string PowerShellCall(string command) =>
        command.StartsWith('"') ? $"& {command}" : command;

    private static string? FirstExisting(params string[] paths)
    {
        foreach (var p in paths)
            if (File.Exists(p)) return p;
        return null;
    }

    private static string? WhereExe(string exe)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("where", exe)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            var line = proc?.StandardOutput.ReadLine()?.Trim();
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch (Exception ex) { LogService.Warn(ex, "Recherche d'un exécutable via where.exe"); return null; }
    }
}
