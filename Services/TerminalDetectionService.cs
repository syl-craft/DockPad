using System.Diagnostics;
using System.IO;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Le terminal par défaut des Options, lancé par les prédéfinis et par toutes les tuiles Terminal.
/// </summary>
/// <remarks>
/// Tous les terminaux, détectés ou ajoutés, se décrivent de la même façon : un programme et deux
/// modèles d'arguments, l'un pour ouvrir un dossier, l'autre pour y lancer aussi une commande.
/// </remarks>
public static class TerminalDetectionService
{
    public const string WindowsTerminalId   = "wt";
    public const string PowerShell7Id       = "pwsh";
    public const string WindowsPowerShellId = "powershell";

    /// <summary>
    /// Valeur du réglage quand le terminal est un programme ajouté dans les Options.
    /// </summary>
    public const string CustomId = "custom";

    public const string FolderToken            = "{folder}";
    public const string CommandToken           = "{command}";
    public const string PowerShellCommandToken = "{command-ps}";

    /// <summary>
    /// Terminaux installés, avec les arguments de DockPad, dans l'ordre du mode Automatique.
    /// </summary>
    public static List<TerminalInfo> Detect()
    {
        var list = new List<TerminalInfo>();

        // Windows Terminal : paquet AppX, sinon une installation portable dans le PATH
        var wtPath = FirstExisting(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe"))
            ?? FindInPath("wt.exe", Environment.GetEnvironmentVariable("PATH") ?? "");
        if (wtPath != null)
            list.Add(Known(WindowsTerminalId, "Windows Terminal", wtPath));

        // PowerShell 7
        var pwshPath = FirstExisting(
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe") ?? WhereExe("pwsh.exe");
        if (pwshPath != null)
            list.Add(Known(PowerShell7Id, "PowerShell 7 (pwsh)", pwshPath));

        // Windows PowerShell
        var poshPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(poshPath))
            list.Add(Known(WindowsPowerShellId, "Windows PowerShell", poshPath));

        return list;
    }

    /// <summary>
    /// Un terminal connu avec les arguments de DockPad.
    /// </summary>
    public static TerminalInfo Known(string id, string name, string exePath)
    {
        var templates = DefaultTemplates(id);
        return new TerminalInfo
        {
            Id = id, DisplayName = name, ExePath = exePath,
            FolderArgs = templates.FolderArgs, CommandArgs = templates.CommandArgs,
        };
    }

    /// <summary>
    /// Arguments de DockPad pour un terminal ; un programme inconnu ne reçoit que le dossier.
    /// </summary>
    public static TerminalSettings DefaultTemplates(string id) => id switch
    {
        WindowsTerminalId => new()
        {
            FolderArgs  = "-w 0 new-tab --startingDirectory \"{folder}\"",
            CommandArgs = "-w 0 new-tab --startingDirectory \"{folder}\" -- {command}",
        },
        PowerShell7Id or WindowsPowerShellId => new()
        {
            FolderArgs  = "-NoExit -Command \"Set-Location '{folder}'\"",
            CommandArgs = "-NoExit -Command \"Set-Location '{folder}'; {command-ps}\"",
        },
        _ => new()
        {
            FolderArgs  = "\"{folder}\"",
            CommandArgs = "\"{folder}\"",
        },
    };

    /// <summary>
    /// Identifiant du terminal connu dont c'est le programme, ou <see cref="CustomId"/>.
    /// </summary>
    public static string KnownIdOf(string exePath) => Path.GetFileName(exePath).ToLowerInvariant() switch
    {
        "wt.exe"         => WindowsTerminalId,
        "pwsh.exe"       => PowerShell7Id,
        "powershell.exe" => WindowsPowerShellId,
        _                => CustomId,
    };

    /// <summary>
    /// Le terminal avec les arguments modifiés dans les Options, s'il y en a.
    /// </summary>
    public static TerminalInfo WithTemplates(TerminalInfo terminal, TerminalSettings? edited) => edited == null
        ? terminal
        : new TerminalInfo
        {
            Id = terminal.Id, DisplayName = terminal.DisplayName, ExePath = terminal.ExePath,
            FolderArgs = edited.FolderArgs, CommandArgs = edited.CommandArgs,
        };

    /// <summary>
    /// Le programme ajouté dans les Options, ou <c>null</c> sans programme.
    /// </summary>
    public static TerminalInfo? CustomTerminal(TerminalSettings custom) => string.IsNullOrWhiteSpace(custom.ExePath)
        ? null
        : new TerminalInfo
        {
            Id = CustomId, DisplayName = custom.ExePath, ExePath = custom.ExePath.Trim().Trim('"'),
            FolderArgs = custom.FolderArgs.Trim(), CommandArgs = custom.CommandArgs.Trim(),
        };

    /// <summary>
    /// Le programme ajouté s'il existe sur le disque, sinon <c>null</c>. Son chemin garde ses
    /// variables d'environnement, que le registre développe lui-même.
    /// </summary>
    public static TerminalInfo? ExistingCustomTerminal(TerminalSettings custom) =>
        CustomTerminal(custom) is { } terminal && File.Exists(Environment.ExpandEnvironmentVariables(terminal.ExePath))
            ? terminal
            : null;

    /// <summary>
    /// Le terminal choisi s'il est disponible, sinon le premier installé : vide, inconnu, désinstallé
    /// ou programme ajouté introuvable (<paramref name="custom"/> nul) valent Automatique.
    /// </summary>
    public static TerminalInfo? Resolve(string setting, TerminalInfo? custom, IReadOnlyList<TerminalInfo> installed)
    {
        var chosen = setting == CustomId
            ? custom
            : installed.Where(t => t.Id == setting).FirstOrDefault();

        return chosen ?? installed.FirstOrDefault();
    }

    /// <summary>
    /// Terminaux installés, avec les arguments modifiés dans <paramref name="templates"/>.
    /// </summary>
    public static List<TerminalInfo> Installed(IReadOnlyDictionary<string, TerminalSettings> templates) =>
        Detect().Select(t => WithTemplates(t, templates.GetValueOrDefault(t.Id))).ToList();

    /// <summary>
    /// Le terminal à lancer selon les Options, ou <c>null</c> si aucun n'est installé.
    /// </summary>
    public static TerminalInfo? ResolveDefault() =>
        Resolve(SettingsService.LoadDefaultTerminal(),
                ExistingCustomTerminal(SettingsService.LoadCustomTerminal()),
                Installed(SettingsService.LoadTerminalTemplates()));

    /// <summary>
    /// Arguments qui ouvrent <paramref name="terminal"/> dans le dossier de la config et y lancent sa commande.
    /// </summary>
    /// <remarks>
    /// L'onglet et les arguments supplémentaires d'une tuile ne sont pas lus : le modèle du terminal décide.
    /// </remarks>
    public static string BuildArgs(TerminalInfo terminal, TerminalConfig cfg)
    {
        string command = cfg.RunCommand.Trim();
        string template = command == "" ? terminal.FolderArgs : terminal.CommandArgs;

        return Expand(template, cfg.StartingDirectory, command);
    }

    /// <summary>
    /// Le modèle avec <c>{folder}</c>, <c>{command}</c> et <c>{command-ps}</c> remplacés.
    /// </summary>
    public static string Expand(string template, string folder, string command) =>
        template.Replace(FolderToken, folder)
                .Replace(PowerShellCommandToken, ForPowerShell(command))
                .Replace(CommandToken, command)
                .Trim();

    /// <summary>
    /// La commande telle qu'elle doit figurer dans un <c>-Command "…"</c> de PowerShell : guillemets
    /// échappés, et <c>&amp;</c> devant un chemin entre guillemets, qu'il afficherait sinon sans l'exécuter.
    /// </summary>
    private static string ForPowerShell(string command)
    {
        string call = command.StartsWith('"') ? $"& {command}" : command;
        return call.Replace("\"", "\\\"");
    }

    /// <summary>
    /// Premier <paramref name="exeName"/> trouvé dans les dossiers de <paramref name="pathVariable"/>.
    /// </summary>
    public static string? FindInPath(string exeName, string pathVariable)
    {
        foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string full = Path.Combine(dir.Trim().Trim('"'), exeName);
                if (File.Exists(full)) return full;
            }
            catch (Exception ex) { LogService.Warn(ex, $"Entrée de PATH invalide ignorée : {dir}"); }
        }
        return null;
    }

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
