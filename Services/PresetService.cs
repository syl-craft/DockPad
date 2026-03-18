using System.Drawing;
using System.IO;
using WinContextMenuManager.Models;

namespace WinContextMenuManager.Services;

public static class PresetService
{
    public static List<PresetEntry> GetPresets()
    {
        return
        [
            BuildClaudeTerminal(),
            BuildPowerShell(),
            BuildVSCode(),
            BuildSSMS(),
        ];
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

        string command;
        if (wt != null)
        {
            command = $"\"{wt}\" -w 0 new-tab --startingDirectory \"%V\" -- claude";
        }
        else
        {
            string? ps = FindExe("powershell.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"));
            command = $"\"{ps ?? "powershell.exe"}\" -NoExit -Command \"Set-Location '%V'; claude\"";
        }

        return new PresetEntry
        {
            DisplayName = "Ouvrir un terminal Claude",
            RegistryKey = "OpenClaudeTerminal",
            Command = command,
            IconPath = icon,
            Target = ContextMenuTarget.FolderBackground,
            Description = "Ouvre un terminal dans ce dossier et lance Claude Code"
        };
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
            DisplayName = "Ouvrir dans PowerShell",
            RegistryKey = "OpenWithPowerShell",
            Command = command,
            IconPath = icon,
            Target = ContextMenuTarget.FolderBackground,
            Description = "Ouvre un onglet PowerShell dans ce dossier"
        };
    }

    private static PresetEntry BuildVSCode()
    {
        string? exe = FindExe("code.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Microsoft VS Code\Code.exe"),
            @"C:\Program Files\Microsoft VS Code\Code.exe");

        string command = exe != null ? $"\"{exe}\" \"%V\"" : "code \"%V\"";
        string icon = exe ?? "";

        return new PresetEntry
        {
            DisplayName = "Ouvrir dans Visual Studio Code",
            RegistryKey = "OpenWithVSCode",
            Command = command,
            IconPath = icon,
            Target = ContextMenuTarget.FolderBackground,
            Description = "Ouvre ce dossier dans Visual Studio Code"
        };
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

        string command = exe != null ? $"\"{exe}\" \"%V\"" : "ssms.exe \"%V\"";
        string icon = exe ?? "";

        return new PresetEntry
        {
            DisplayName = "Ouvrir dans SQL Server Management Studio",
            RegistryKey = "OpenWithSSMS",
            Command = command,
            IconPath = icon,
            Target = ContextMenuTarget.FolderBackground,
            Description = "Ouvre ce dossier dans SQL Server Management Studio"
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
            catch { }
        }

        return null;
    }
}
