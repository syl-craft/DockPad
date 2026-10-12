using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// <c>DockPad.exe --terminal "&lt;dossier&gt;" -- &lt;commande&gt;</c> : ce que lancent les prédéfinis terminal
/// du clic droit, pour ouvrir le terminal exactement comme une tuile (fenêtre et couleur du dossier).
/// </summary>
/// <remarks>
/// <para>
/// <b>Traité avant WPF</b>, dans <c>Program.Main</c> : un clic droit ne démarre jamais l'application.
/// Une instance résidente reçoit la demande par un tube et lance le terminal ; sans elle, le
/// processus court calcule et lance lui-même. Mesures dans NIN-149.
/// </para>
/// <para>
/// Le registre passe le dossier sous la forme <c>"%V\."</c> : un <c>"D:\"</c> nu ferait de <c>\"</c>
/// un guillemet échappé, et le reste de la ligne rejoindrait le dossier.
/// </para>
/// </remarks>
public static class TerminalLaunch
{
    public const string Flag = "--terminal";
    public const string PipeName = "DockPad_TerminalPipe";

    /// <summary>
    /// Un dossier à ouvrir, et la commande à y lancer (vide pour une simple console).
    /// </summary>
    public readonly record struct Request(string Folder, string Command);

    /// <summary>
    /// La demande portée par la ligne de commande, ou <c>null</c> si ce n'est pas un lancement <c>--terminal</c>.
    /// </summary>
    public static Request? Parse(string[] args)
    {
        int flag = Array.IndexOf(args, Flag);
        if (flag < 0 || flag + 1 >= args.Length) return null;

        string folder = NormalizeFolder(args[flag + 1]);
        int separator = Array.IndexOf(args, "--", flag + 2);
        string command = separator < 0 ? "" : JoinArguments(args.Skip(separator + 1));

        return new Request(folder, command);
    }

    /// <summary>
    /// Le dossier sans son <c>\.</c> final, et une racine de lecteur gardée avec sa barre.
    /// </summary>
    public static string NormalizeFolder(string folder)
    {
        string trimmed = folder.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    /// <summary>
    /// Recompose des arguments en une ligne de commande : ceux qui portent une espace ou un guillemet
    /// sont remis entre guillemets, comme Windows les a découpés.
    /// </summary>
    public static string JoinArguments(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg != "" && !arg.Any(c => c is ' ' or '\t' or '"')) return arg;

        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            // Les barres qui précèdent un guillemet sont doublées, le guillemet échappé.
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }
        quoted.Append('\\', backslashes * 2).Append('"');
        return quoted.ToString();
    }

    /// <summary>
    /// La demande en une ligne pour le tube : le dossier, une tabulation, la commande. Un chemin
    /// Windows ne peut pas contenir de tabulation.
    /// </summary>
    public static string Encode(Request request) => $"{request.Folder}\t{request.Command}";

    public static Request? Decode(string payload)
    {
        int tab = payload.IndexOf('\t');
        return tab < 0 ? null : new Request(payload[..tab], payload[(tab + 1)..]);
    }

    /// <summary>
    /// Ce que lance la demande dans <paramref name="terminal"/>, ou <c>null</c> sans terminal : les
    /// variables se calculent sur le dossier, comme pour une tuile sans nom.
    /// </summary>
    public static ShortcutLauncher.LaunchPlan? Plan(TerminalInfo? terminal, Request request) => terminal == null
        ? null
        : new ShortcutLauncher.LaunchPlan(terminal.ExePath, TerminalDetectionService.BuildArgs(terminal,
            new TerminalConfig { StartingDirectory = request.Folder, RunCommand = request.Command }));

    /// <summary>
    /// Traite un lancement <c>--terminal</c> ; faux si la ligne de commande n'en est pas un.
    /// </summary>
    /// <remarks>
    /// Rien n'est journalisé ici : le journal n'est pas initialisé, et l'initialiser coûterait ce
    /// que ce chemin existe pour éviter. Un échec se voit à l'écran.
    /// </remarks>
    public static bool TryHandle(string[] args)
    {
        if (Parse(args) is not { } request) return false;

        if (StartupRelay.InstanceIsRunning(StartupRelay.SingleInstanceMutex)
            && new LinePipeService(PipeName).TrySendSilently(Encode(request), timeoutMs: 500))
            return true;

        Launch(request);
        return true;
    }

    /// <summary>
    /// Lance le terminal par défaut sur la demande ; sans terminal ou en cas d'échec, un message.
    /// </summary>
    public static void Launch(Request request)
    {
        try
        {
            if (Plan(TerminalDetectionService.ResolveDefault(), request) is not { } plan)
            {
                ShowError(Localization.Loc.T("Quick_NoTerminal"));
                return;
            }
            ShortcutLauncher.Start(plan);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    private const uint MB_ICONWARNING = 0x30;

    // Une boîte Win32 et non AppDialog : aucune Application WPF n'existe sur ce chemin.
    private static void ShowError(string message) => MessageBoxW(IntPtr.Zero, message, "DockPad", MB_ICONWARNING);
}
