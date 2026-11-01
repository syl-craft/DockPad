using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace DockPad.Services.Updates;

public sealed record UpdateBlockerClassification(bool IsMcpRelay, string? Program);

/// <summary>Classification et groupes purs ; aucune consultation des processus ici.</summary>
public static class UpdateBlockerGroups
{
    public static bool IsStableLauncher(string? image, string directory) =>
        !string.IsNullOrEmpty(image) && Path.GetFileName(image).Equals("DockPad.exe", StringComparison.OrdinalIgnoreCase)
        && !AppInstallation.IsWithin(image, directory);

    public static UpdateBlockerClassification Classify(string image, string directory, string? commandLine,
        string? parentName, string? parentPath, string? grandparentName = null, string? grandparentPath = null)
    {
        var relay = image.Length > 0 && AppInstallation.IsWithin(image, directory)
            && commandLine != null && HasMcpArgument(commandLine);
        if (!relay) return new(false, Path.GetFileNameWithoutExtension(image));
        // Le lanceur Velopack attend l'enfant current/ : le client MCP est son propre parent.
        if (IsStableLauncher(parentPath, directory))
        {
            parentName = grandparentName;
            parentPath = grandparentPath;
        }
        if (string.IsNullOrWhiteSpace(parentName)) return new(true, null);
        if (parentName.Equals("claude.exe", StringComparison.OrdinalIgnoreCase))
        {
            var desktop = parentPath != null && (parentPath.Contains("AnthropicClaude", StringComparison.OrdinalIgnoreCase)
                || parentPath.Contains("Claude Desktop", StringComparison.OrdinalIgnoreCase));
            return new(true, desktop ? "Claude Desktop" : "Claude Code");
        }
        return new(true, Path.GetFileNameWithoutExtension(parentName));
    }

    private static bool HasMcpArgument(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero) return false;
        try
        {
            // argv[0] est l'image, pas un argument de StartupRelay.
            for (var i = 1; i < count; i++)
                if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) == "--mcp") return true;
            return false;
        }
        finally { LocalFree(argv); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    public static IReadOnlyList<UpdateBlockerGroup> Group(IReadOnlyList<UpdateBlocker> blockers) => blockers
        .GroupBy(b => (b.IsMcpRelay, Program: b.IsMcpRelay ? "" : b.Name), new GroupKeyComparer())
        .OrderByDescending(g => g.Key.IsMcpRelay)
        .ThenBy(g => g.Key.Program, StringComparer.CurrentCultureIgnoreCase)
        .Select(g => new UpdateBlockerGroup(g.Key.IsMcpRelay, g.Key.Program, g.OrderBy(b => b.Pid).ToArray()))
        .ToArray();

    // Le nom d'un programme est insensible à la casse, comme ses fichiers sous Windows.
    private sealed class GroupKeyComparer : IEqualityComparer<(bool IsMcpRelay, string Program)>
    {
        public bool Equals((bool IsMcpRelay, string Program) x, (bool IsMcpRelay, string Program) y) =>
            x.IsMcpRelay == y.IsMcpRelay && StringComparer.OrdinalIgnoreCase.Equals(x.Program, y.Program);
        public int GetHashCode((bool IsMcpRelay, string Program) key) =>
            HashCode.Combine(key.IsMcpRelay, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Program));
    }
}

public sealed class UpdateBlockerGroup(bool isMcpRelay, string program, IReadOnlyList<UpdateBlocker> processes) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsMcpRelay => isMcpRelay;
    public string Name => isMcpRelay ? Loc.T("Update_McpGroup") : program;
    public string Help => Loc.T(isMcpRelay ? "Update_McpHelp" : "Update_ProgramHelp");
    public IReadOnlyList<UpdateBlocker> Processes => processes;
    public int Count => processes.Count;
    public bool? Selected
    {
        get => UpdateBlockers.SelectionState(processes);
        set
        {
            foreach (var process in processes) process.Selected = value == true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }
}
