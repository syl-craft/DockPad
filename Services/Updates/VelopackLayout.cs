using System.IO;

namespace DockPad.Services.Updates;

public static class VelopackLayout
{
    public static string? FindRoot(string executable)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(executable)) ?? throw new ArgumentException("Executable has no directory", nameof(executable));
        if (!string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase)) return null;
        var root = Path.GetDirectoryName(dir) ?? throw new ArgumentException("Installation has no root", nameof(executable));
        return File.Exists(Path.Combine(dir, "sq.version")) && File.Exists(Path.Combine(root, "Update.exe"))
            ? root : null;
    }
    public static string ResolveExecutable(string executable)
    {
        var root = FindRoot(executable);
        var stable = root is null ? null : Path.Combine(root, Path.GetFileName(executable));
        return stable is not null && File.Exists(stable) ? stable : executable;
    }
    public static bool IsWithin(string path, string directory) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(
            Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
