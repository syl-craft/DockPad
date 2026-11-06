using Mue.Velopack;
using System.IO;
using System.Runtime.InteropServices;
using Mue;

namespace DockPad.Services.UpdateHost;

public sealed class DockPadBlockerClassifier : IUpdateBlockerClassifier
{
    public bool IsIgnored(string? image, string directory) =>
        !string.IsNullOrEmpty(image) && Path.GetFileName(image).Equals("DockPad.exe", StringComparison.OrdinalIgnoreCase)
        && !VelopackLayout.IsWithin(image, directory);

    public UpdateBlockerClassification Classify(UpdateBlockerContext context)
    {
        var (image, directory, commandLine, parentName, parentPath, grandparentName, grandparentPath) = context;
        var relay = image.Length > 0 && VelopackLayout.IsWithin(image, directory)
            && commandLine != null && HasMcpArgument(commandLine);
        if (!relay) return new(false, Path.GetFileNameWithoutExtension(image));
        // Le lanceur Velopack attend l'enfant current/ : le client MCP est son propre parent.
        if (IsIgnored(parentPath, directory))
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
            // argv[0] désigne l'image et ne participe pas à la recherche d'arguments.
            for (var i = 1; i < count; i++)
                if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) == "--mcp") return true;
            return false;
        }
        finally { LocalFree(argv); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

}
