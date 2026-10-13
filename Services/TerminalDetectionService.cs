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
    public const string WezTermId           = "wezterm";

    /// <summary>
    /// Identifiants des terminaux que DockPad sait détecter : réservés, un terminal ajouté ne les prend pas.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownIds =
        [WindowsTerminalId, PowerShell7Id, WindowsPowerShellId, WezTermId];

    public const string FolderToken            = "{folder}";
    public const string CommandToken           = "{command}";
    public const string PowerShellCommandToken = "{command-ps}";
    public const string NameToken              = "{name}";
    public const string FolderNameToken        = "{folder-name}";
    public const string ColorToken             = "{color}";

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

        // WezTerm : wezterm-gui.exe, qui n'ouvre pas de console en plus de sa fenêtre
        var wezPath = FirstExisting(@"C:\Program Files\WezTerm\wezterm-gui.exe")
            ?? FindInPath("wezterm-gui.exe", Environment.GetEnvironmentVariable("PATH") ?? "");
        if (wezPath != null)
            list.Add(Known(WezTermId, "WezTerm", wezPath));


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
            FolderArgs  = "-w \"{folder-name}\" new-tab --tabColor {color} --startingDirectory \"{folder}\"",
            CommandArgs = "-w \"{folder-name}\" new-tab --tabColor {color} --startingDirectory \"{folder}\" -- {command}",
        },
        PowerShell7Id or WindowsPowerShellId => new()
        {
            FolderArgs  = "-NoExit -Command \"Set-Location '{folder}'\"",
            CommandArgs = "-NoExit -Command \"Set-Location '{folder}'; {command-ps}\"",
        },
        WezTermId => new()
        {
            FolderArgs  = "start --new-tab --cwd \"{folder}\"",
            CommandArgs = "start --new-tab --cwd \"{folder}\" -- {command}",
        },

        _ => new()
        {
            FolderArgs  = "\"{folder}\"",
            CommandArgs = "\"{folder}\"",
        },
    };

    /// <summary>
    /// Identifiant du terminal connu dont c'est le programme, ou vide pour un programme inconnu.
    /// </summary>
    public static string KnownIdOf(string exePath) => Path.GetFileName(exePath).ToLowerInvariant() switch
    {
        "wt.exe"                         => WindowsTerminalId,
        "pwsh.exe"                       => PowerShell7Id,
        "powershell.exe"                 => WindowsPowerShellId,
        "wezterm-gui.exe" or "wezterm.exe" => WezTermId,
        _                                => "",
    };

    /// <summary>
    /// Vrai si le programme existe ; son chemin peut porter des variables d'environnement.
    /// </summary>
    public static bool ProgramExists(string exePath) =>
        exePath.Trim() != "" && File.Exists(Environment.ExpandEnvironmentVariables(exePath.Trim().Trim('"')));

    /// <summary>
    /// Le terminal choisi s'il est proposé, sinon le premier proposé : vide, inconnu, masqué ou
    /// introuvable valent Automatique.
    /// </summary>
    public static TerminalInfo? Resolve(string setting, IReadOnlyList<TerminalInfo> installed)
    {
        var chosen = installed.Where(t => t.Id == setting).FirstOrDefault();

        return chosen ?? installed.FirstOrDefault();
    }

    /// <summary>
    /// Le terminal à lancer selon les Options, ou <c>null</c> si aucun n'est proposé.
    /// </summary>
    public static TerminalInfo? ResolveDefault() =>
        Resolve(SettingsService.LoadDefaultTerminal(),
                TerminalCatalog.Available(TerminalConfigService.EnsureInitialized().Terminals, ProgramExists));

    /// <summary>
    /// Arguments qui ouvrent <paramref name="terminal"/> dans le dossier de la config et y lancent sa commande.
    /// </summary>
    /// <remarks>
    /// L'onglet et les arguments supplémentaires d'une tuile ne sont pas lus : le modèle du terminal décide.
    /// <paramref name="name"/> est le nom de la tuile ; vide, <c>{name}</c> vaut le nom du dossier.
    /// </remarks>
    public static string BuildArgs(TerminalInfo terminal, TerminalConfig cfg, string name = "")
    {
        string command = cfg.RunCommand.Trim();
        string template = command == "" ? terminal.FolderArgs : terminal.CommandArgs;

        return Expand(template, cfg.StartingDirectory, command, name, cfg.TabColor, QuotingFor(terminal.ExePath));
    }

    /// <summary>
    /// Comment le programme lancé relit une valeur insérée dans sa ligne de commande.
    /// </summary>
    public enum ValueQuoting
    {
        /// <summary>Arguments directs : un nom de dossier ne peut pas contenir de <c>"</c>, rien à faire.</summary>
        None,

        /// <summary>Windows Terminal coupe ses sous-commandes sur <c>;</c>, même entre guillemets.</summary>
        WindowsTerminal,

        /// <summary>PowerShell : la valeur vit entre guillemets simples dans un <c>-Command</c>.</summary>
        PowerShell,
    }

    /// <summary>
    /// La règle d'échappement du programme, d'après le nom de son exécutable : un terminal ajouté
    /// à la main qui lance <c>pwsh.exe</c> est protégé comme celui que DockPad a détecté.
    /// </summary>
    public static ValueQuoting QuotingFor(string exePath) => Path.GetFileName(exePath).ToLowerInvariant() switch
    {
        "wt.exe" or "windowsterminal.exe" => ValueQuoting.WindowsTerminal,
        "pwsh.exe" or "powershell.exe"    => ValueQuoting.PowerShell,
        _                                 => ValueQuoting.None,
    };

    private static readonly System.Text.RegularExpressions.Regex TokenPattern =
        new(@"\{(folder-name|folder|name|color|command-ps|command)\}");

    /// <summary>
    /// Le modèle avec <c>{folder}</c>, <c>{folder-name}</c>, <c>{name}</c>, <c>{color}</c>,
    /// <c>{command}</c> et <c>{command-ps}</c> remplacés.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Le dossier vient du clic droit, donc d'un nom que n'importe quelle archive peut choisir.</b>
    /// Les valeurs — dossier, nom du dossier, nom de la tuile — sont échappées selon
    /// <paramref name="quoting"/>, le programme qui relira la ligne ; le modèle, lui, n'est jamais
    /// touché. La commande ne l'est pas non plus : elle vient de la configuration de l'utilisateur, et
    /// un <c>;</c> y garde son sens de séparateur pour Windows Terminal.
    /// </para>
    /// <para>
    /// <b>Une seule passe</b> : remplacer jeton après jeton relisait les valeurs déjà insérées, et un
    /// dossier nommé <c>{command}</c> recevait la commande.
    /// </para>
    /// </remarks>
    public static string Expand(string template, string folder, string command, string name = "", string color = "",
        ValueQuoting quoting = ValueQuoting.None)
    {
        string folderName = FolderName(folder);
        string tabColor = IsTabColor(color) ? color.Trim() : TabColor(folderName);
        string tileName = string.IsNullOrWhiteSpace(name) ? folderName : name.Trim();

        string expanded = TokenPattern.Replace(template, token => token.Value switch
        {
            FolderNameToken        => BeforeClosingQuote(Escape(folderName, quoting)),
            FolderToken            => BeforeClosingQuote(Escape(folder, quoting)),
            NameToken              => BeforeClosingQuote(Escape(tileName, quoting)),
            ColorToken             => tabColor,
            PowerShellCommandToken => ForPowerShell(command),
            _                      => command,
        });

        return expanded.Trim();
    }

    /// <summary>
    /// La valeur telle que <paramref name="quoting"/> la relira littéralement.
    /// </summary>
    /// <remarks>
    /// Pour PowerShell, les guillemets simples typographiques sont doublés eux aussi : PowerShell
    /// les lit comme des <c>'</c>, et un dossier nommé <c>p’; calc; #</c> sortirait sinon de la chaîne.
    /// </remarks>
    private static string Escape(string value, ValueQuoting quoting) => quoting switch
    {
        ValueQuoting.WindowsTerminal => value.Replace(";", "\\;"),
        ValueQuoting.PowerShell      => PowerShellSingleQuoted(value),
        _                            => value,
    };

    private static string PowerShellSingleQuoted(string value)
    {
        var escaped = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            escaped.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') escaped.Append(c);
        }
        return escaped.ToString();
    }

    /// <summary>
    /// Teintes distinctes, assez soutenues pour se lire sur un onglet clair comme sombre.
    /// </summary>
    public static readonly IReadOnlyList<string> TabColors =
    [
        "#1A73E8", "#188038", "#D93025", "#E37400", "#9334E6",
        "#12B5CB", "#E52592", "#F9AB00", "#00897B", "#7CB342",
    ];

    /// <summary>
    /// Couleur d'onglet d'un dossier, toujours la même pour un même nom, d'une session à l'autre.
    /// </summary>
    /// <remarks>
    /// FNV-1a sur le nom en minuscules, et non <c>GetHashCode</c>, qui change à chaque démarrage.
    /// </remarks>
    public static string TabColor(string folderName)
    {
        uint hash = 2166136261;
        foreach (char c in folderName.ToLowerInvariant())
        {
            hash ^= c;
            hash *= 16777619;
        }
        return TabColors[(int)(hash % (uint)TabColors.Count)];
    }

    /// <summary>
    /// Vrai pour une couleur que Windows Terminal accepte : <c>#RGB</c> ou <c>#RRGGBB</c>.
    /// </summary>
    public static bool IsTabColor(string color) =>
        System.Text.RegularExpressions.Regex.IsMatch(color.Trim(), "^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$");

    /// <summary>
    /// Le dernier segment du dossier ; une racine de lecteur, ou <c>%V</c> qui n'est connu qu'au clic,
    /// reste entier.
    /// </summary>
    public static string FolderName(string folder)
    {
        string trimmed = folder.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        return name == "" ? trimmed : name;
    }

    /// <summary>
    /// La valeur avec ses barres finales doublées : devant le guillemet fermant d'un modèle
    /// (<c>"{folder}"</c>), le <c>\</c> de <c>D:\</c> échapperait sinon ce guillemet.
    /// </summary>
    private static string BeforeClosingQuote(string value)
    {
        int trailing = value.Length - value.TrimEnd('\\').Length;
        return trailing == 0 ? value : value + new string('\\', trailing);
    }

    /// <summary>
    /// La commande telle qu'elle doit figurer dans un <c>-Command "…"</c> de PowerShell : un
    /// exécutable entre guillemets est appelé par <c>&amp;</c>, qu'il afficherait sinon sans
    /// l'exécuter, et les autres guillemets sont échappés.
    /// </summary>
    /// <remarks>
    /// L'exécutable passe en guillemets <b>simples</b> : entre guillemets doubles, PowerShell
    /// développerait un <c>$(…)</c> de son chemin. Les arguments qui suivent gardent leurs guillemets
    /// doubles — ils viennent de la configuration de l'utilisateur, qui peut y vouloir une variable.
    /// </remarks>
    private static string ForPowerShell(string command)
    {
        int closing = command.StartsWith('"') ? command.IndexOf('"', 1) : -1;
        if (closing < 0) return command.Replace("\"", "\\\"");

        string exe = command[1..closing];
        string arguments = command[(closing + 1)..];
        return $"& '{PowerShellSingleQuoted(exe)}'{arguments.Replace("\"", "\\\"")}";
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
