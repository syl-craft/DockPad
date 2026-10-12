using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Le terminal par défaut des Options : lequel est lancé, et avec quelle commande.
/// </summary>
/// <remarks>
/// Un seul réglage décide du terminal des prédéfinis et de toutes les tuiles Terminal. Les deux
/// chemins passent par les mêmes fonctions, donc les mêmes tests.
/// </remarks>
public class DefaultTerminalTests
{
    private static readonly TerminalInfo Wt =
        new() { Id = TerminalDetectionService.WindowsTerminalId, ExePath = @"C:\wt.exe" };
    private static readonly TerminalInfo Pwsh =
        new() { Id = TerminalDetectionService.PowerShell7Id, ExePath = @"C:\pwsh.exe" };
    private static readonly TerminalInfo WindowsPowerShell =
        new() { Id = TerminalDetectionService.WindowsPowerShellId, ExePath = @"C:\powershell.exe" };

    // -- Quel terminal ---------------------------------------------------------

    [Fact]
    public void Automatique_PrendLePremierDansLOrdreDeDetection()
    {
        var resolved = TerminalDetectionService.Resolve("", [Wt, Pwsh, WindowsPowerShell]);

        Assert.Same(Wt, resolved);
    }

    [Fact]
    public void Automatique_SansWindowsTerminal_PrendPowerShell7()
    {
        var resolved = TerminalDetectionService.Resolve("", [Pwsh, WindowsPowerShell]);

        Assert.Same(Pwsh, resolved);
    }

    [Fact]
    public void TerminalChoisi_EstRetenuSIlEstInstalle()
    {
        var resolved = TerminalDetectionService.Resolve("powershell", [Wt, Pwsh, WindowsPowerShell]);

        Assert.Same(WindowsPowerShell, resolved);
    }

    [Theory]
    [InlineData("pwsh")]      // choisi puis désinstallé
    [InlineData("cmd")]       // retiré de la liste, écrit par une version antérieure
    [InlineData("inconnu")]   // écrit par une version plus récente
    public void TerminalChoisiAbsent_SeComporteCommeAutomatique(string setting)
    {
        var resolved = TerminalDetectionService.Resolve(setting, [Wt, WindowsPowerShell]);

        Assert.Same(Wt, resolved);
    }

    [Fact]
    public void AucunTerminalInstalle_NeRendRien()
    {
        Assert.Null(TerminalDetectionService.Resolve("", []));
    }

    // -- Avec quelle commande --------------------------------------------------

    [Fact]
    public void PowerShell_LanceUnCheminEntreGuillemetsParLOperateurDAppel()
    {
        // Sans « & », PowerShell lit un chemin entre guillemets comme une chaîne et l'affiche au
        // lieu de l'exécuter. Les guillemets sont échappés : ils vivent dans un -Command lui-même
        // entre guillemets, qu'ils fermeraient sinon.
        var cfg = new TerminalConfig
        {
            StartingDirectory = "%V",
            RunCommand = @"""C:\Program Files\OpenAI\codex.exe""",
        };

        var args = TerminalDetectionService.BuildArgs(Pwsh, cfg);

        Assert.Equal(@"-NoExit -Command ""Set-Location '%V'; & \""C:\Program Files\OpenAI\codex.exe\""""", args);
    }

    [Fact]
    public void PowerShell_EchappeLesGuillemetsDesArguments()
    {
        // Le cas des tuiles qui reprennent une session : --remote-control "Train de Noël".
        var cfg = new TerminalConfig
        {
            StartingDirectory = @"C:\dev",
            RunCommand = @"claude --remote-control ""Train de Noël""",
        };

        var args = TerminalDetectionService.BuildArgs(WindowsPowerShell, cfg);

        Assert.Equal(@"-NoExit -Command ""Set-Location 'C:\dev'; claude --remote-control \""Train de Noël\""""", args);
    }

    [Fact]
    public void WindowsTerminal_PasseLaCommandeApresLeSeparateur()
    {
        var cfg = new TerminalConfig
        {
            StartingDirectory = @"C:\dev",
            RunCommand = @"claude --remote-control ""Train de Noël""",
            NewTab = true,
        };

        var args = TerminalDetectionService.BuildArgs(Wt, cfg);

        Assert.Equal(@"-w 0 new-tab --startingDirectory ""C:\dev"" -- claude --remote-control ""Train de Noël""", args);
    }
}
