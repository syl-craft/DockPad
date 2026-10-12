using System.IO;
using System.Runtime.InteropServices;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// <c>DockPad.exe --terminal "&lt;dossier&gt;" -- &lt;commande&gt;</c> : ce que lancent les prédéfinis terminal
/// du clic droit, pour ouvrir le terminal exactement comme une tuile (fenêtre et couleur du dossier).
/// </summary>
/// <remarks>
/// <para>
/// <b>Traité avant WPF</b>, dans <c>Program.Main</c> : un clic droit ne démarre jamais l'application.
/// Le registre appelle d'abord le lanceur natif <c>DockPadTerminal.exe</c>, qui relaie la demande à
/// l'instance résidente ; c'est ce processus-ci qui la traite quand il n'y en a pas, ou quand le
/// lanceur manque. Mesures dans NIN-149.
/// </para>
/// <para>
/// Le registre passe le dossier sous la forme <c>"%V\."</c> : un <c>"D:\"</c> nu ferait de <c>\"</c>
/// un guillemet échappé, et le reste de la ligne rejoindrait le dossier.
/// </para>
/// </remarks>
public static class TerminalLaunch
{
    /// <summary>
    /// Ce que lance la demande dans <paramref name="terminal"/>, ou <c>null</c> sans terminal : les
    /// variables se calculent sur le dossier, comme pour une tuile sans nom.
    /// </summary>
    public static ShortcutLauncher.LaunchPlan? Plan(TerminalInfo? terminal, TerminalRequest.Request request) => terminal == null
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
    public static bool TryHandle(string[] args) => TryHandle(args,
        instanceIsRunning: () => StartupRelay.InstanceIsRunning(TerminalRequest.SingleInstanceMutex),
        send: line => new LinePipeService(TerminalRequest.PipeName).TrySendSilently(line, timeoutMs: 500),
        launch: Launch);

    /// <summary>
    /// Même traitement, avec l'instance, le tube et le lancement fournis par l'appelant.
    /// </summary>
    public static bool TryHandle(string[] args, Func<bool> instanceIsRunning, Func<string, bool> send,
        Action<TerminalRequest.Request> launch)
    {
        if (TerminalRequest.Parse(args) is not { } request) return false;

        if (instanceIsRunning() && send(TerminalRequest.Encode(request))) return true;

        launch(request);
        return true;
    }

    /// <summary>
    /// Vrai si ce processus tourne en administrateur.
    /// </summary>
    public static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Lance le terminal par défaut sur la demande ; sans terminal ou en cas d'échec, un message.
    /// </summary>
    public static void Launch(TerminalRequest.Request request)
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
