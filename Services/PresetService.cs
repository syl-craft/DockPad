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

        string? powershell = FindExe("powershell.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"));

        string command = CodexCommand(wt, powershell, FindCodexExe());

        return new PresetEntry
        {
            DisplayName = Loc.T("Preset_CodexTerminal_Name"),
            RegistryKey = "OpenCodexTerminal",
            Command = command,
            // Le logo ChatGPT d'abord : c'est le seul visuel qui identifie vraiment l'entrée.
            // À défaut, le binaire de Codex — qui n'embarque aucune icône, donc celle d'un
            // exécutable quelconque. À défaut encore, rien.
            IconPath = ChatGptIconPath() ?? FindCodexExe() ?? "",
            Target = ContextMenuTarget.FolderBackground,
            Description = Loc.T("Preset_CodexTerminal_Desc")
        };
    }

    /// <summary>
    /// La commande du prédéfini Codex : Windows Terminal si possible, sinon PowerShell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Le chemin absolu quand on le connaît, et ce n'est pas un raffinement</b> : lancer
    /// <c>codex</c> par son nom nu a donné <i>« erreur 0x80070002, le fichier spécifié est
    /// introuvable »</i>. Le nom ne se résout que si le <c>PATH</c> du <b>lanceur</b> est à jour —
    /// or c'est l'Explorateur qui lance, et il transmet le <c>PATH</c> qu'il avait à son propre
    /// démarrage. Une installation faite depuis, et le prédéfini échoue jusqu'à la prochaine
    /// session Windows.
    /// </para>
    /// <para>
    /// C'est le patron que <c>BuildFolderPreset</c> applique déjà à VS Code et SSMS avec son
    /// <c>fallbackExe</c> : le chemin résolu d'abord, le nom nu en repli — lequel reste utile sur
    /// une machine où DockPad ne sait pas localiser le binaire.
    /// </para>
    /// </remarks>
    public static string CodexCommand(string? wt, string? powershell, string? codexExe)
    {
        string codex = codexExe is null ? "codex" : $"\"{codexExe}\"";

        return wt != null
            ? $"\"{wt}\" -w 0 new-tab --startingDirectory \"%V\" -- {codex}"
            : $"\"{powershell ?? "powershell.exe"}\" -NoExit -Command \"Set-Location '%V'; & {codex}\"";
    }

    /// <summary>
    /// Le logo ChatGPT, extrait <b>une seule fois</b> dans le store d'icônes du profil.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi une copie, et pas le paquet directement.</b> Le seul exécutable qui porte le
    /// logo vit sous <c>C:\Program Files\WindowsApps\OpenAI.ChatGPT-Desktop_<i>version</i>_…</c> :
    /// ce chemin part dans le registre, et deviendrait faux à la prochaine mise à jour de ChatGPT —
    /// laissant une icône cassée dans le menu contextuel de Windows. Le fichier du profil, lui, ne
    /// bouge jamais, et survit même à la désinstallation de l'application.
    /// </para>
    /// <para>
    /// <b>Un <c>.ico</c> et non un <c>.png</c></b>, contrairement au store des tuiles : le shell
    /// Windows ne sait pas lire un PNG pour l'icône d'une entrée de menu.
    /// </para>
    /// <para>
    /// L'extraction a lieu au premier appel et pas ensuite. <c>GetPresets</c> écrit donc un fichier,
    /// ce qui n'est pas anodin pour un accesseur — mais le geste est unique, gardé par un
    /// <c>File.Exists</c>, et le faire à l'installation obligerait le statut affiché à mentir
    /// jusque-là.
    /// </para>
    /// </remarks>
    private static string? ChatGptIconPath()
    {
        var target = Path.Combine(AppPaths.ProfileRoot, "icons", "chatgpt.ico");
        if (File.Exists(target)) return target;

        if (FindChatGptExe() is not { } exe) return null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // 64 px : le menu contextuel affiche 16 ou 24 px selon la mise à l'échelle, et
            // réduire une image nette vaut mieux qu'agrandir une petite.
            using var icon = Icon.ExtractIcon(exe, 0, IconSize) ?? Icon.ExtractAssociatedIcon(exe);
            if (icon is null) return null;

            using var bitmap = icon.ToBitmap();
            using var png = new MemoryStream();
            bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);

            File.WriteAllBytes(target, BuildIco(png.ToArray(), bitmap.Width));
            return target;
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Extraction du logo ChatGPT");
            return null;
        }
    }

    private const int IconSize = 64;

    /// <summary>
    /// Un fichier <c>.ico</c> contenant une seule image, au format PNG.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>Icon.Save</c> abîme la transparence</b>, et ce n'est pas une supposition : mesuré sur
    /// le logo ChatGPT, <b>zéro</b> pixel bleuté à l'extraction, <b>quarante-cinq</b> après
    /// l'aller-retour — un halo visible autour du dessin. On écrit donc le conteneur soi-même.
    /// </para>
    /// <para>
    /// Le shell lit les icônes à image PNG depuis Vista, et ce format préserve l'alpha exactement,
    /// là où le DIB classique le reconstruit à partir d'un masque.
    /// </para>
    /// </remarks>
    public static byte[] BuildIco(byte[] png, int size)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((ushort)0);   // réservé
        w.Write((ushort)1);   // type : icône
        w.Write((ushort)1);   // une seule image

        // Un octet ne peut pas porter 256 : le format code cette taille par zéro.
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);     // couleurs de palette
        w.Write((byte)0);     // réservé
        w.Write((ushort)1);   // plans
        w.Write((ushort)32);  // bits par pixel
        w.Write(png.Length);
        w.Write(6 + 16);      // les données suivent l'en-tête et l'unique entrée

        w.Write(png);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// L'exécutable du paquet ChatGPT, retrouvé par le registre.
    /// </summary>
    /// <remarks>
    /// <b><c>C:\Program Files\WindowsApps</c> ne s'énumère pas</b> — ses ACL l'interdisent, même en
    /// lecture. Mais Windows publie la racine de chaque paquet installé sous une clé <b>par
    /// utilisateur</b>, lisible sans privilège, et dont le nom porte la version : on y lit
    /// <c>PackageRootFolder</c> plutôt que de deviner un chemin.
    /// </remarks>
    private static string? FindChatGptExe()
    {
        const string repository =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        try
        {
            using var packages = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(repository);
            if (packages is null) return null;

            foreach (var name in packages.GetSubKeyNames()
                         .Where(n => n.StartsWith("OpenAI.ChatGPT", StringComparison.OrdinalIgnoreCase)))
            {
                using var package = packages.OpenSubKey(name);
                if (package?.GetValue("PackageRootFolder") is not string root) continue;

                // Plusieurs versions peuvent rester déclarées ; seule celle dont le dossier existe
                // encore est utilisable.
                var app = Path.Combine(root, "app");
                if (!Directory.Exists(app)) continue;

                if (PickChatGptExe(Directory.EnumerateFiles(app, "*.exe")) is { } exe) return exe;
            }
        }
        catch (Exception ex) { LogService.Warn(ex, "Recherche du paquet ChatGPT"); }

        return null;
    }

    /// <summary>
    /// Lequel des exécutables d'un paquet ChatGPT porte le logo.
    /// </summary>
    /// <remarks>
    /// Le paquet n'en expose qu'un aujourd'hui, mais rien ne le garantit demain : un utilitaire de
    /// mise à jour posé à côté, et l'on extrairait <b>son</b> icône sans le voir. À défaut de nom
    /// reconnaissable on prend le premier — une icône plausible vaut mieux qu'aucune.
    /// </remarks>
    public static string? PickChatGptExe(IEnumerable<string> exeFiles)
    {
        var all = exeFiles.ToList();

        return all.FirstOrDefault(f => Path.GetFileName(f)
                       .Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault();
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
