using System.Diagnostics;
using System.IO;
using WinContextMenuManager.Models;

namespace WinContextMenuManager.Services;

public static class TerminalDetectionService
{
    public static List<TerminalInfo> Detect()
    {
        var list = new List<TerminalInfo>();

        // Windows Terminal (AppX package)
        var wtPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(wtPath))
            list.Add(new TerminalInfo { DisplayName = "Windows Terminal", ExePath = wtPath, SupportsNewTab = true });

        // PowerShell 7
        var pwshPath = FirstExisting(
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe") ?? WhereExe("pwsh.exe");
        if (pwshPath != null)
            list.Add(new TerminalInfo { DisplayName = "PowerShell 7 (pwsh)", ExePath = pwshPath });

        // Windows PowerShell
        var poshPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(poshPath))
            list.Add(new TerminalInfo { DisplayName = "Windows PowerShell", ExePath = poshPath });

        // Invite de commandes
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (File.Exists(cmdPath))
            list.Add(new TerminalInfo { DisplayName = "Invite de commandes (cmd)", ExePath = cmdPath });

        // Git Bash
        var gitBash = FirstExisting(
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe");
        if (gitBash != null)
            list.Add(new TerminalInfo { DisplayName = "Git Bash", ExePath = gitBash });

        return list;
    }

    /// <summary>Construit les arguments à passer au terminal selon sa config.</summary>
    public static string BuildArgs(TerminalConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.ExePath)) return "";

        return Path.GetFileNameWithoutExtension(cfg.ExePath).ToLowerInvariant() switch
        {
            "wt"         => BuildWtArgs(cfg),
            "pwsh"       => BuildPwshArgs(cfg),
            "powershell" => BuildPwshArgs(cfg),
            "cmd"        => BuildCmdArgs(cfg),
            _            => cfg.ExtraArgs.Trim(),
        };
    }

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

    private static string BuildPwshArgs(TerminalConfig cfg)
    {
        var cmds = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.StartingDirectory))
            cmds.Add($"Set-Location '{cfg.StartingDirectory}'");
        if (!string.IsNullOrWhiteSpace(cfg.RunCommand))
            cmds.Add(cfg.RunCommand.Trim());

        var parts = new List<string> { "-NoExit" };
        if (cmds.Count > 0)
            parts.Add($"-Command \"{string.Join("; ", cmds)}\"");
        if (!string.IsNullOrWhiteSpace(cfg.ExtraArgs))
            parts.Add(cfg.ExtraArgs.Trim());
        return string.Join(" ", parts);
    }

    private static string BuildCmdArgs(TerminalConfig cfg)
    {
        var cmds = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.StartingDirectory))
            cmds.Add($"cd /d \"{cfg.StartingDirectory}\"");
        if (!string.IsNullOrWhiteSpace(cfg.RunCommand))
            cmds.Add(cfg.RunCommand.Trim());
        if (!string.IsNullOrWhiteSpace(cfg.ExtraArgs))
            cmds.Add(cfg.ExtraArgs.Trim());
        return cmds.Count > 0 ? $"/k \"{string.Join(" & ", cmds)}\"" : "";
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
        catch { return null; }
    }
}
