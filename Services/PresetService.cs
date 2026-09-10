using System.Diagnostics;
using System.Drawing;
using System.IO;
using DockPad.Models;

namespace DockPad.Services;

public static class PresetService
{
    public static List<PresetEntry> GetPresets()
    {
        PresetEntry?[] presets =
        [
            BuildClaudeTerminal(),
            BuildCodexTerminal(),
            BuildPowerShell(),
            BuildVSCode(),
            BuildSSMS(),
            BuildGitHubDesktop(),
        ];

        // Certains prédéfinis (GitHub Desktop) ne sont proposés que si l'application
        // cible est réellement installée — null = non disponible sur cette machine.
        return presets.OfType<PresetEntry>().ToList();
    }

    private static PresetEntry BuildClaudeTerminal()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Find Claude app icon
        string? claudeExe = FindExe("claude.exe",
            Path.Combine(localAppData, @"AnthropicClaude\claude.exe"),
            Path.Combine(localAppData, @"Programs\claude\claude.exe"),
            @"C:\Program Files\Anthropic\Claude\claude.exe");

        string icon = claudeExe ?? "";

        // Prefer Windows Terminal with -w 0 to reuse existing window
        string? wt = FindExe("wt.exe",
            Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe"));

        string claudeArgs = SettingsService.LoadClaudeArgs();
        string claudeCmd  = string.IsNullOrEmpty(claudeArgs) ? "claude" : $"claude {claudeArgs}";

        string command;
        if (wt != null)
        {
            command = $"\"{wt}\" -w 0 new-tab --startingDirectory \"%V\" -- {claudeCmd}";
        }
        else
        {
            string? ps = FindExe("powershell.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"));
            command = $"\"{ps ?? "powershell.exe"}\" -NoExit -Command \"Set-Location '%V'; {claudeCmd}\"";
        }

        return new PresetEntry
        {
            DisplayName = Loc.T("Preset_ClaudeTerminal_Name"),
            RegistryKey = "OpenClaudeTerminal",
            Command = command,
            IconPath = icon,
            Target = ContextMenuTarget.FolderBackground,
            Description = Loc.T("Preset_ClaudeTerminal_Desc")
        };
    }

    /// <summary>
    /// Décalque du prédéfini Claude, pour Codex.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proposé sans condition</b>, comme celui de Claude et contrairement à GitHub Desktop : le
    /// prédéfini pose une entrée de menu, il n'installe rien. Une machine sans Codex verra la
    /// commande échouer à l'usage, ce qui est le comportement déjà accepté pour Claude.
    /// </para>
    /// <para>
    /// <b>Pas de réglage d'arguments supplémentaires</b>, contrairement à Claude : celui-ci existe
    /// parce qu'un besoin réel l'a demandé. Un champ vide de plus dans les Options n'aiderait
    /// personne tant que rien n'a à y être écrit.
    /// </para>
    /// </remarks>
    private static PresetEntry BuildCodexTerminal()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string? wt = FindExe("wt.exe",
            Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe"));

        string command = wt != null
            ? $"\"{wt}\" -w 0 new-tab --startingDirectory \"%V\" -- codex"
            : $"\"{FindExe("powershell.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) ?? "powershell.exe"}\" -NoExit -Command \"Set-Location '%V'; codex\"";

        return new PresetEntry
        {
            DisplayName = Loc.T("Preset_CodexTerminal_Name"),
            RegistryKey = "OpenCodexTerminal",
            Command = command,
            IconPath = FindCodexExe() ?? "",
            Target = ContextMenuTarget.FolderBackground,
            Description = Loc.T("Preset_CodexTerminal_Desc")
        };
    }

    /// <summary>
    /// Le binaire natif de Codex, dont l'icône du prédéfini est tirée : le <c>PATH</c>, puis
    /// les racines d'installation, puis — en dernier — le paquet npm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Codex s'installe de plusieurs façons, et elles ne posent pas le binaire au même
    /// endroit.</b> Une installation native l'ajoute au <c>PATH</c> ; par WinGet il vit sous un
    /// dossier qui nomme la version, donc on cherche <b>dessous</b> plutôt que de le nommer. Même
    /// patron que la localisation de <c>bw.exe</c>, pour la même raison : un chemin complet devient
    /// faux à la première mise à jour.
    /// </para>
    /// <para>
    /// <b>Le paquet npm vient en dernier, et il est le seul cas où le <c>PATH</c> ne suffit
    /// pas</b> : il n'y met que <c>codex.cmd</c>, un script, et cache le vrai exécutable sous un
    /// <c>vendor</c> qui nomme la plateforme.
    /// </para>
    /// <para>
    /// <b>L'exécutable n'embarque aujourd'hui aucune icône</b> : Windows affiche celle d'un
    /// exécutable quelconque. On le pointe quand même — le jour où OpenAI en posera une, elle
    /// apparaîtra sans qu'on touche à ce code. Introuvable, l'icône reste vide et l'entrée
    /// s'affiche sans visuel.
    /// </para>
    /// </remarks>
    public static string? FindCodexExe(string pathVariable, IEnumerable<string> installRoots,
                                       IEnumerable<string> npmRoots)
    {
        foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Le PATH d'une machine réelle porte des entrées mortes et des caractères illégaux.
            }
        }

        // Le PATH d'un processus est fige a son demarrage : Codex installe pendant que DockPad
        // tourne n'y apparait pas. On regarde donc aussi le disque, sous les racines ou les
        // installateurs rangent le binaire — chacune nommant une version dans un sous-dossier.
        foreach (var root in installRoots)
            if (Search(root) is { } installed) return installed;

        foreach (var root in npmRoots)
            if (Search(Path.Combine(root,
                    @"node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor"))
                is { } fromNpm)
                return fromNpm;

        return null;

        static string? Search(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
            try
            {
                return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
                                .FirstOrDefault();
            }
            catch (Exception ex)
            {
                LogService.Warn(ex, $"Recherche de codex.exe sous {root}");
                return null;
            }
        }
    }

    /// <summary>Les emplacements réels de cette machine.</summary>
    private static string? FindCodexExe()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return FindCodexExe(
            Environment.GetEnvironmentVariable("PATH") ?? "",
            [
                Path.Combine(local, @"Programs\OpenAI"),          // installateur natif
                Path.Combine(local, @"Microsoft\WinGet\Packages"), // WinGet
            ],
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
            ]);
    }

    /// <summary>
    /// Statut d'un prédéfini face à ce que porte le registre.
    /// </summary>
    /// <remarks>
    /// Le <b>nom affiché</b> entre dans la comparaison, au même titre que la commande et l'icône :
    /// c'est ce qui change quand DockPad change de langue. Sans lui, une entrée installée dans
    /// l'autre langue s'annonçait « déjà installée » et le bouton refusait de la réappliquer — la
    /// traduction du menu contextuel de Windows était alors inatteignable depuis l'interface.
    /// </remarks>
    public static PresetStatus CompareStatus(
        (string DisplayName, string Command, string Icon)? installed, PresetEntry preset)
    {
        if (installed is not { } current) return PresetStatus.NotInstalled;

        return current.DisplayName == preset.DisplayName
            && current.Command == preset.Command
            && current.Icon == preset.IconPath
                ? PresetStatus.UpToDate
                : PresetStatus.UpdateAvailable;
    }

    private static PresetEntry BuildPowerShell()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);

        // Prefer Windows Terminal with -w 0 to reuse existing window
        string? wt = FindExe("wt.exe", Path.Combine(localAppData, @"Microsoft\WindowsApps\wt.exe"));

        string? psExe = FindExe("pwsh.exe",
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files\PowerShell\7-preview\pwsh.exe")
            ?? FindExe("powershell.exe",
                Path.Combine(system, @"WindowsPowerShell\v1.0\powershell.exe"));

        string command;
        if (wt != null)
        {
            command = $"\"{wt}\" -w 0 new-tab --startingDirectory \"%V\"";
        }
        else
        {
            command = $"\"{psExe ?? "powershell.exe"}\" -NoExit -Command \"Set-Location '%V';\"";
        }

        string icon = psExe ?? "";

        return new PresetEntry
        {
            DisplayName = Loc.T("Preset_PowerShell_Name"),
            RegistryKey = "OpenWithPowerShell",
            Command = command,
            IconPath = icon,
            Target = ContextMenuTarget.FolderBackground,
            Description = Loc.T("Preset_PowerShell_Desc")
        };
    }

    private static PresetEntry BuildVSCode()
    {
        string? exe = FindExe("code.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Microsoft VS Code\Code.exe"),
            @"C:\Program Files\Microsoft VS Code\Code.exe");

        return BuildFolderPreset(
            Loc.T("Preset_VSCode_Name"), "OpenWithVSCode",
            Loc.T("Preset_VSCode_Desc"),
            exe, fallbackExe: "code");
    }

    private static PresetEntry BuildSSMS()
    {
        // Scan dynamically to support any version (18, 19, 20, 21, ...)
        string ssmsRoot = @"C:\Program Files (x86)";
        string[] ssmsCandidates = Directory.Exists(ssmsRoot)
            ? Directory.GetDirectories(ssmsRoot, "Microsoft SQL Server Management Studio *")
                       .OrderByDescending(d => d)
                       .Select(d => Path.Combine(d, @"Common7\IDE\Ssms.exe"))
                       .ToArray()
            : [];

        string? exe = FindExe("Ssms.exe", ssmsCandidates);

        return BuildFolderPreset(
            Loc.T("Preset_Ssms_Name"), "OpenWithSSMS",
            Loc.T("Preset_Ssms_Desc"),
            exe, fallbackExe: "ssms.exe");
    }

    private static PresetEntry? BuildGitHubDesktop()
    {
        // GitHub Desktop est une install Squirrel strictement per-user
        // (%LocalAppData%\GitHubDesktop) : jamais sur le PATH ni dans Program Files.
        string exePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"GitHubDesktop\GitHubDesktop.exe");

        // Pas de repli sur un nom nu : `GitHubDesktop.exe` n'est résolvable nulle part
        // et le flag --cli-open n'est compris que par cet exe. Sans install → pas de prédéfini.
        if (!File.Exists(exePath)) return null;

        // --cli-open= n'existe que depuis la refonte CLI de GitHub Desktop 3.4.14 ;
        // les versions antérieures ignorent silencieusement le switch (l'app s'ouvre
        // sans charger le dossier).
        var version = FileVersionInfo.GetVersionInfo(exePath);
        if (new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart)
            < new Version(3, 4, 14))
            return null;

        // Appel direct du flag interne `--cli-open=` : c'est exactement ce que le shim
        // `github` (bin\github.bat → cli.js) finit par exécuter — il relance
        // `GitHubDesktop.exe --cli-open=<chemin>` en mode GUI. En l'appelant directement,
        // on ajoute ET ouvre le dépôt sans passer par cmd/.bat, donc aucune fenêtre
        // console ne reste ouverte (notamment au premier lancement, cold boot Electron).
        //
        // Le chemin est laissé en %LocalAppData% (écrit en REG_EXPAND_SZ par
        // RegistryService) : la clé HKCR est machine-wide alors que l'install est
        // per-user — chaque compte (y compris l'admin élevé qui installe le prédéfini)
        // doit résoudre son propre profil au moment du clic.
        //
        // Le suffixe `\.` neutralise le backslash final des racines de lecteur :
        // %V → `D:\` produirait `--cli-open="D:\"` où `\"` devient une quote échappée.
        // GitHub Desktop canonise ensuite le chemin (git rev-parse --show-toplevel).
        return new PresetEntry
        {
            DisplayName = Loc.T("Preset_GitHubDesktop_Name"),
            RegistryKey = "OpenWithGitHubDesktop",
            Command = @"""%LocalAppData%\GitHubDesktop\GitHubDesktop.exe"" --cli-open=""%V\.""",
            IconPath = @"%LocalAppData%\GitHubDesktop\GitHubDesktop.exe",
            Target = ContextMenuTarget.FolderBackground,
            Description = Loc.T("Preset_GitHubDesktop_Desc")
        };
    }

    /// <summary>Prédéfini standard « "exe" "%V" » : exe résolu, sinon repli sur le nom nu.</summary>
    private static PresetEntry BuildFolderPreset(string displayName, string registryKey,
        string description, string? exe, string fallbackExe)
    {
        return new PresetEntry
        {
            DisplayName = displayName,
            RegistryKey = registryKey,
            Command = exe != null ? $"\"{exe}\" \"%V\"" : $"{fallbackExe} \"%V\"",
            IconPath = exe ?? "",
            Target = ContextMenuTarget.FolderBackground,
            Description = description
        };
    }

    private static string? FindExe(string exeName, params string[] candidates)
    {
        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        // Try PATH
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                string full = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(full)) return full;
            }
            catch (Exception ex) { LogService.Warn(ex, $"Entrée de PATH invalide ignorée : {dir}"); }
        }

        return null;
    }
}
