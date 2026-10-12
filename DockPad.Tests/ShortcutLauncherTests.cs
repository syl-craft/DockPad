using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Ce qu'une tuile lance, et avec quels arguments.
/// </summary>
/// <remarks>
/// Cette logique vivait dans le code-behind de la fenêtre, mêlée au <c>Process.Start</c> et au
/// dialogue d'erreur : elle n'avait donc aucun test, alors qu'elle décide de ce qui s'exécute sur la
/// machine de l'utilisateur. Séparer la <b>décision</b> (testable) de l'<b>effet</b>
/// (<c>Process.Start</c>, non testable) est tout l'objet de l'extraction.
/// </remarks>
public class ShortcutLauncherTests
{
    private static ShortcutEntry Entry(ShortcutType type, string command) =>
        new() { Name = "t", Type = type, Command = command };

    [Fact]
    public void Dossier_PasseParLExplorateurAvecUnCheminEntreGuillemets()
    {
        // Sans guillemets, « C:\Mes documents » se coupe à l'espace et l'Explorateur ouvre « C:\Mes ».
        var plan = ShortcutLauncher.Plan(Entry(ShortcutType.OpenFolder, @"C:\Mes documents"), terminal: null);

        Assert.Equal("explorer.exe", plan!.Value.FileName);
        Assert.Equal("\"C:\\Mes documents\"", plan.Value.Arguments);
    }

    [Fact]
    public void Url_EstLanceeTelleQuelle()
    {
        // L'URL est le programme : c'est le shell qui choisit le navigateur.
        var plan = ShortcutLauncher.Plan(Entry(ShortcutType.OpenUrl, "https://claude.ai"), terminal: null);

        Assert.Equal("https://claude.ai", plan!.Value.FileName);
        Assert.Equal("", plan.Value.Arguments);
    }

    [Theory]
    [InlineData("notepad.exe", "notepad.exe", "")]
    [InlineData("code C:\\dev", "code", "C:\\dev")]
    [InlineData("\"C:\\Program Files\\app.exe\" --flag", "C:\\Program Files\\app.exe", "--flag")]
    [InlineData("  notepad.exe  ", "notepad.exe", "")]
    public void Commande_SepareLExeDeSesArguments(string command, string exe, string args)
    {
        // Le cas qui compte : un chemin entre guillemets contenant des espaces ne doit pas se couper
        // à la première espace.
        var plan = ShortcutLauncher.Plan(Entry(ShortcutType.RunCommand, command), terminal: null);

        Assert.Equal(exe, plan!.Value.FileName);
        Assert.Equal(args, plan.Value.Arguments);
    }

    private static readonly TerminalInfo Wt =
        TerminalDetectionService.Known(TerminalDetectionService.WindowsTerminalId, "WindowsTerminalId", @"C:\wt.exe");
    private static readonly TerminalInfo Pwsh =
        TerminalDetectionService.Known(TerminalDetectionService.PowerShell7Id, "PowerShell7Id", @"C:\pwsh.exe");

    [Fact]
    public void Terminal_LanceLeTerminalDesOptions_PasCeluiEnregistreDansLaTuile()
    {
        // Le programme enregistré dans la tuile n'est plus lu : il reste dans le fichier pour un
        // retour arrière de version, mais c'est le réglage qui décide.
        var entry = Entry(ShortcutType.OpenTerminal, @"C:\dev");
        entry.Terminal = new TerminalConfig { ExePath = @"C:\wt.exe", StartingDirectory = @"C:\dev", RunCommand = "claude" };

        var plan = ShortcutLauncher.Plan(entry, Pwsh);

        Assert.Equal(@"C:\pwsh.exe", plan!.Value.FileName);
        Assert.Equal(@"-NoExit -Command ""Set-Location 'C:\dev'; claude""", plan.Value.Arguments);
    }

    [Fact]
    public void Terminal_EnAutomatique_LanceExactementCeQuAvant()
    {
        // Les tuiles existantes enregistrent wt.exe : avec Windows Terminal en automatique, rien ne
        // doit changer, ni le programme ni un seul caractère des arguments.
        var entry = Entry(ShortcutType.OpenTerminal, @"C:\dev\DockPad");
        entry.Terminal = new TerminalConfig
        {
            ExePath = @"C:\wt.exe",
            StartingDirectory = @"C:\dev\DockPad",
            RunCommand = @"claude --resume abc --remote-control ""DockPad""",
            NewTab = true,
        };

        var plan = ShortcutLauncher.Plan(entry, Wt);

        Assert.Equal(@"C:\wt.exe", plan!.Value.FileName);
        Assert.Equal(@"-w 0 new-tab --startingDirectory ""C:\dev\DockPad"" -- claude --resume abc --remote-control ""DockPad""",
                     plan.Value.Arguments);
    }

    [Fact]
    public void Terminal_AncienFormat_OuvreLeDossierDansLeTerminalDesOptions()
    {
        // Ancien format : seul le dossier est connu, porté par la commande de la tuile.
        var plan = ShortcutLauncher.Plan(Entry(ShortcutType.OpenTerminal, @"C:\dev"), Wt);

        Assert.Equal(@"C:\wt.exe", plan!.Value.FileName);
        Assert.Equal(@"-w 0 new-tab --startingDirectory ""C:\dev""", plan.Value.Arguments);
    }

    [Fact]
    public void Terminal_SansAucunTerminalInstalle_NaPasDePlan()
    {
        Assert.Null(ShortcutLauncher.Plan(Entry(ShortcutType.OpenTerminal, @"C:\dev"), terminal: null));
    }

    [Fact]
    public void BasculeVersProcessus_NaPasDePlan()
    {
        // Ce type ne lance pas un processus « à l'aveugle » : il cherche d'abord une fenêtre
        // existante, ce que fait ProcessSwitchService.
        var entry = Entry(ShortcutType.SwitchToProcess, "devenv.exe");
        entry.ProcessSwitch = new ProcessSwitchConfig { ProcessName = "devenv.exe" };

        Assert.Null(ShortcutLauncher.Plan(entry, terminal: null));
    }
}
