using System.Diagnostics;
using System.IO;
using DockPad.Services.Updates;

namespace DockPad.Services.UpdateHost;

/// <summary>
/// Le point d'entrée stable reste hors de current/, y compris pour les clients MCP stdio.
/// </summary>
public static class AppInstallation
{
    private static string ProcessPath => Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable");
    public static string Executable => ResolveExecutable(ProcessPath);
    public static string? Root => FindRoot(ProcessPath);
    public static string? FindRoot(string executable) => VelopackLayout.FindRoot(executable);
    public static string ResolveExecutable(string executable) => VelopackLayout.ResolveExecutable(executable);
    public static bool IsWithin(string path, string directory) => VelopackLayout.IsWithin(path, directory);

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
        // Le lanceur attend hors de current/ ; aucune URL n'est écrite sur disque ni journalisée.
        using var child = Process.Start(start) ?? throw new IOException("Cannot start the stable launcher");
        // Attendre ici retiendrait l'image de current/ et empêcherait son remplacement.
        return true;
    }
}
