using System.Diagnostics;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Ce qu'une tuile lance, et comment.
/// </summary>
/// <remarks>
/// <para>
/// <b>La décision est séparée de l'effet.</b> <see cref="Plan"/> dit quoi lancer et avec quels
/// arguments — c'est du calcul pur, donc testable ; <see cref="Launch"/> appelle
/// <c>Process.Start</c>. Cette logique vivait dans le code-behind de la fenêtre, mêlée au dialogue
/// d'erreur : elle n'avait aucun test alors qu'elle décide de ce qui s'exécute sur la machine.
/// </para>
/// <para>
/// <b>Aucune dépendance WPF ici</b>, pas même pour signaler une erreur : <see cref="Launch"/> laisse
/// remonter l'exception, et c'est l'appelant — la vue — qui décide de l'afficher. Un service qui
/// ouvre lui-même un dialogue ne se teste plus et ne se réutilise pas.
/// </para>
/// </remarks>
public static class ShortcutLauncher
{
    /// <summary>Un programme et ses arguments, prêts pour <c>Process.Start</c>.</summary>
    public readonly record struct LaunchPlan(string FileName, string Arguments);

    /// <summary>
    /// Plan de lancement d'une entrée, ou <c>null</c> quand il n'y en a pas un seul : une bascule
    /// vers un processus cherche d'abord une fenêtre existante, et une tuile Terminal n'a rien à
    /// lancer sans <paramref name="terminal"/>, celui des Options.
    /// </summary>
    public static LaunchPlan? Plan(ShortcutEntry entry, TerminalInfo? terminal) => entry.Type switch
    {
        // Les guillemets ne sont pas décoratifs : sans eux « C:\Mes documents » se coupe à l'espace
        // et l'Explorateur ouvre « C:\Mes ».
        ShortcutType.OpenFolder => new LaunchPlan("explorer.exe", $"\"{entry.Command}\""),

        // L'URL est le programme : c'est le shell qui choisit le navigateur.
        ShortcutType.OpenUrl => new LaunchPlan(entry.Command, ""),

        // Le programme enregistré dans la tuile n'est pas lu : toutes suivent le terminal des Options.
        ShortcutType.OpenTerminal => terminal == null
            ? null
            : new LaunchPlan(terminal.ExePath, TerminalDetectionService.BuildArgs(terminal, TerminalConfigOf(entry), entry.Name)),

        ShortcutType.SwitchToProcess => null,

        _ => SplitCommand(entry.Command),
    };

    /// <summary>
    /// Dossier et commande d'une tuile Terminal ; sans dossier, le dossier utilisateur, que les
    /// modèles de terminal attendent toujours. Une tuile à l'ancien format n'a que son dossier.
    /// </summary>
    private static TerminalConfig TerminalConfigOf(ShortcutEntry entry)
    {
        var cfg = entry.Terminal ?? new TerminalConfig { StartingDirectory = entry.Command };
        string folder = string.IsNullOrWhiteSpace(cfg.StartingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : cfg.StartingDirectory;

        return new TerminalConfig { StartingDirectory = folder, RunCommand = cfg.RunCommand, TabColor = cfg.TabColor };
    }

    /// <summary>
    /// Lance l'entrée. Laisse remonter toute exception : l'affichage de l'erreur appartient à la vue.
    /// </summary>
    public static void Launch(ShortcutEntry entry)
    {
        if (entry.Type == ShortcutType.SwitchToProcess)
        {
            if (entry.ProcessSwitch is { } switchConfig)
                ProcessSwitchService.SwitchOrLaunch(switchConfig);
            return;
        }

        var terminal = entry.Type == ShortcutType.OpenTerminal ? TerminalDetectionService.ResolveDefault() : null;

        if (Plan(entry, terminal) is not { } plan)
            throw new InvalidOperationException(Localization.Loc.T("Quick_NoTerminal"));

        Start(plan);
    }

    /// <summary>
    /// Lance une ligne de commande brute — celle d'une entrée du menu contextuel de dossier, où
    /// <c>%V</c> a déjà été substitué. Laisse remonter l'exception, comme <see cref="Launch"/>.
    /// </summary>
    public static void RunCommandLine(string command) => Start(SplitCommand(command));

    /// <summary>
    /// Ouvre <paramref name="terminal"/> sur un dossier et y lance une commande : le bouton Tester des Options.
    /// </summary>
    public static void OpenTerminal(TerminalInfo terminal, string folder, string command) =>
        Start(new LaunchPlan(terminal.ExePath, TerminalDetectionService.BuildArgs(terminal,
            new TerminalConfig { StartingDirectory = folder, RunCommand = command })));

    // Le programme d'un terminal personnalisé peut porter %LocalAppData%, que Process.Start ne développe pas.
    private static void Start(LaunchPlan plan) =>
        Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(plan.FileName), plan.Arguments)
        {
            UseShellExecute = true,
        });

    /// <summary>
    /// Sépare l'exécutable de ses arguments. Un chemin entre guillemets peut contenir des espaces :
    /// couper à la première espace casserait « "C:\Program Files\app.exe" --flag ».
    /// </summary>
    /// <remarks>
    /// Publique parce qu'elle sert aussi à lire une ligne de commande du registre — celle du
    /// navigateur par défaut, dont on ne veut que l'exécutable pour en extraire l'icône. Deux
    /// analyseurs de ligne de commande dans le même projet finiraient par diverger.
    /// </remarks>
    public static LaunchPlan SplitCommand(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return new LaunchPlan(command[1..end], command[(end + 1)..].Trim());
        }

        int space = command.IndexOf(' ');
        return space > 0
            ? new LaunchPlan(command[..space], command[(space + 1)..])
            : new LaunchPlan(command, "");
    }
}
