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
        var plan = ShortcutLauncher.Plan(Entry(ShortcutType.OpenFolder, @"C:\Mes documents"));

        Assert.Equal("explorer.exe", plan!.Value.FileName);
        Assert.Equal("\"C:\\Mes documents\"", plan.Value.Arguments);
    }

    [Fact]
    public void Url_EstLanceeTelleQuelle()
    {
        // L'URL est le programme : c'est le shell qui choisit le navigateur.
        var plan = ShortcutLauncher.Plan(Entry(ShortcutType.OpenUrl, "https://claude.ai"));

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
        var plan = ShortcutLauncher.Plan(Entry(ShortcutType.RunCommand, command));

        Assert.Equal(exe, plan!.Value.FileName);
        Assert.Equal(args, plan.Value.Arguments);
    }

    [Fact]
    public void Terminal_ConfigureUtiliseSonExeEtSesArguments()
    {
        var entry = Entry(ShortcutType.OpenTerminal, @"C:\dev");
        entry.Terminal = new TerminalConfig { ExePath = "wt.exe", StartingDirectory = @"C:\dev" };

        var plan = ShortcutLauncher.Plan(entry);

        Assert.Equal("wt.exe", plan!.Value.FileName);
        Assert.Contains(@"C:\dev", plan.Value.Arguments);
    }

    [Fact]
    public void Terminal_SansConfiguration_NaPasDePlanUnique()
    {
        // Ancien format : seul le dossier est connu, le terminal se choisit par essais successifs.
        Assert.Null(ShortcutLauncher.Plan(Entry(ShortcutType.OpenTerminal, @"C:\dev")));
    }

    [Fact]
    public void Terminal_SansConfiguration_EssaieLesQuatreDansLOrdre()
    {
        var plans = ShortcutLauncher.TerminalFallbacks(@"C:\dev").ToList();

        Assert.Equal(["wt.exe", "pwsh.exe", "powershell.exe", "cmd.exe"],
                     plans.Select(p => p.FileName));
        Assert.Contains("new-tab", plans[0].Arguments);
        Assert.Contains("Set-Location", plans[1].Arguments);
        Assert.Contains("cd /d", plans[3].Arguments);
        Assert.All(plans, p => Assert.Contains(@"C:\dev", p.Arguments));
    }

    [Fact]
    public void BasculeVersProcessus_NaPasDePlan()
    {
        // Ce type ne lance pas un processus « à l'aveugle » : il cherche d'abord une fenêtre
        // existante, ce que fait ProcessSwitchService.
        var entry = Entry(ShortcutType.SwitchToProcess, "devenv.exe");
        entry.ProcessSwitch = new ProcessSwitchConfig { ProcessName = "devenv.exe" };

        Assert.Null(ShortcutLauncher.Plan(entry));
    }
}
