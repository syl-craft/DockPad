using System.Diagnostics;
using System.IO;

namespace DockPad.Services.Updates;

/// <summary>The stable entry point is outside current/, including for MCP stdio clients.</summary>
public static class AppInstallation
{
    public static string Executable => ResolveExecutable(Environment.ProcessPath!);
    public static string? Root => FindRoot(Environment.ProcessPath!);
    public static string? FindRoot(string executable)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        if (!string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase)) return null;
        var root = Path.GetDirectoryName(dir)!;
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

    public static string? Marker => Root is { } root ? Path.Combine(root, ".dockpad-update") : null;
    public static bool IsUpdating => Marker is { } path && File.Exists(path)
        && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromMinutes(5);

    public static void BeginUpdate()
    {
        if (Marker is not { } path) throw new InvalidOperationException("Not a packaged installation");
        File.WriteAllText(path, "");
    }
    public static void EndUpdate()
    {
        if (Marker is { } path) File.Delete(path);
    }
    public static bool DeferDuringUpdate(string[] args)
    {
        if (!IsUpdating || args.Contains("--update-restarted")) return false;
        if (string.Equals(Executable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Stable launcher is missing");
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        // The root launcher waits outside current/. No URL is written to disk or logged.
        using var child = Process.Start(start) ?? throw new IOException("Cannot start the stable launcher");
        // Never wait while holding the image in current/: that would block its replacement.
        // MCP clients use the root launcher, which owns their stdio lifetime outside current/.
        return true;
    }
}
