using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Un nom de dossier ne doit jamais pouvoir ajouter une commande au lancement du terminal.
/// </summary>
/// <remarks>
/// Le dossier vient du clic droit : c'est le seul morceau de la ligne qu'un tiers peut choisir,
/// en livrant une archive qui contient un dossier au nom piégé. Il est échappé selon le
/// programme qui relit la ligne — Windows Terminal coupe sur <c>;</c> même entre guillemets,
/// PowerShell sort d'une chaîne <c>'…'</c> sur un <c>'</c> —, et non selon le modèle.
/// </remarks>
public class TerminalFolderInjectionTests
{
    private static readonly TerminalInfo Wt =
        TerminalDetectionService.Known(TerminalDetectionService.WindowsTerminalId, "Windows Terminal", @"C:\wt.exe");
    private static readonly TerminalInfo Pwsh =
        TerminalDetectionService.Known(TerminalDetectionService.PowerShell7Id, "PowerShell 7", @"C:\Program Files\PowerShell\7\pwsh.exe");
    private static readonly TerminalInfo WindowsPowerShell =
        TerminalDetectionService.Known(TerminalDetectionService.WindowsPowerShellId, "Windows PowerShell", @"C:\powershell.exe");
    private static readonly TerminalInfo WezTerm =
        TerminalDetectionService.Known(TerminalDetectionService.WezTermId, "WezTerm", @"C:\wezterm-gui.exe");

    private static TerminalConfig Folder(string folder, string command = "") =>
        new() { StartingDirectory = folder, RunCommand = command };

    // -- Windows Terminal ------------------------------------------------------

    [Fact]
    public void WindowsTerminal_PointVirguleDuDossier_EstEchappe()
    {
        var args = TerminalDetectionService.BuildArgs(Wt, Folder(@"C:\dl\a;b", "claude"));

        Assert.Equal(
            $@"-w ""a\;b"" new-tab --tabColor {TerminalDetectionService.TabColor("a;b")} --startingDirectory ""C:\dl\a\;b"" -- claude",
            args);
    }

    [Fact]
    public void WindowsTerminal_DossierPiege_NeCreePasDeSousCommande()
    {
        var args = TerminalDetectionService.BuildArgs(Wt, Folder(@"C:\dl\x;new-tab calc.exe"));

        Assert.DoesNotMatch(@"(?<!\\);", args);
        Assert.Contains(@"--startingDirectory ""C:\dl\x\;new-tab calc.exe""", args);
    }

    [Fact]
    public void WindowsTerminal_NomDeTuile_EstEchappe()
    {
        var wt = new TerminalInfo { Id = "wt", ExePath = @"C:\wt.exe", FolderArgs = @"-w ""{name}"" new-tab" };

        var args = TerminalDetectionService.BuildArgs(wt, Folder(@"C:\dev"), name: "a;b");

        Assert.Equal(@"-w ""a\;b"" new-tab", args);
    }

    [Fact]
    public void WindowsTerminal_LaCommandeDeLaTuileNEstPasTouchee()
    {
        // La commande vient de la configuration de l'utilisateur : un « ; » y garde son sens.
        var args = TerminalDetectionService.BuildArgs(Wt, Folder(@"C:\dev", "claude ; split-pane"));

        Assert.EndsWith("-- claude ; split-pane", args);
    }

    // -- PowerShell -------------------------------------------------------------

    [Fact]
    public void PowerShell_ApostropheDuDossier_EstDoublee()
    {
        var args = TerminalDetectionService.BuildArgs(Pwsh, Folder(@"C:\dl\p'; Write-Output X; #"));

        Assert.Equal(@"-NoExit -Command ""Set-Location 'C:\dl\p''; Write-Output X; #'""", args);
    }

    [Fact]
    public void WindowsPowerShell_ApostropheDuDossier_EstDoubleeAvecUneCommande()
    {
        var args = TerminalDetectionService.BuildArgs(WindowsPowerShell, Folder(@"C:\dl\l'atelier", "claude"));

        Assert.Equal(@"-NoExit -Command ""Set-Location 'C:\dl\l''atelier'; claude""", args);
    }

    /// <summary>PowerShell lit aussi les guillemets simples typographiques comme des <c>'</c>.</summary>
    [Theory]
    [InlineData('\u2018')]
    [InlineData('\u2019')]
    [InlineData('\u201A')]
    [InlineData('\u201B')]
    public void PowerShell_GuillemetSimpleTypographique_EstDouble(char quote)
    {
        var args = TerminalDetectionService.BuildArgs(Pwsh, Folder($@"C:\dl\p{quote}; calc; #"));

        Assert.Equal($@"-NoExit -Command ""Set-Location 'C:\dl\p{quote}{quote}; calc; #'""", args);
    }

    [Fact]
    public void PowerShell_ExecutableEntreGuillemets_PasseEnGuillemetsSimples()
    {
        // Entre guillemets doubles, PowerShell développerait un $(…) du chemin.
        var args = TerminalDetectionService.BuildArgs(Pwsh, Folder(@"C:\dev", @"""C:\a b\x.exe"" run"));

        Assert.Equal(@"-NoExit -Command ""Set-Location 'C:\dev'; & 'C:\a b\x.exe' run""", args);
    }

    [Fact]
    public void PowerShell_ApostropheDuCheminDExecutable_EstDoublee()
    {
        var args = TerminalDetectionService.BuildArgs(Pwsh, Folder(@"C:\dev", @"""C:\l'app\$(calc)\x.exe"""));

        Assert.Equal(@"-NoExit -Command ""Set-Location 'C:\dev'; & 'C:\l''app\$(calc)\x.exe'""", args);
    }

    [Fact]
    public void DossierNommeCommeUnJeton_NEstPasRemplace()
    {
        // Remplacer jeton après jeton relisait les valeurs déjà insérées.
        var args = TerminalDetectionService.BuildArgs(WezTerm, Folder(@"C:\dl\{command}", "claude"));

        Assert.Equal(@"start --new-tab --cwd ""C:\dl\{command}"" -- claude", args);
    }

    /// <summary>
    /// La règle se lit sur le chemin développé, celui que <c>ShortcutLauncher.Start</c> lance.
    /// </summary>
    [Fact]
    public void ProgrammeDonneParUneVariable_EstProtegeSelonLeProgrammeReel()
    {
        Environment.SetEnvironmentVariable("DOCKPAD_TEST_NIN152_TERM", @"C:\Program Files\PowerShell\7\pwsh.exe");
        try
        {
            Assert.Equal(TerminalDetectionService.ValueQuoting.PowerShell,
                TerminalDetectionService.QuotingFor("%DOCKPAD_TEST_NIN152_TERM%"));
        }
        finally { Environment.SetEnvironmentVariable("DOCKPAD_TEST_NIN152_TERM", null); }
    }

    /// <summary>
    /// Le parcours du clic droit, de bout en bout : la ligne que Windows passe au lanceur
    /// (<c>--terminal "%V\." -- commande</c>) jusqu'aux arguments du terminal.
    /// </summary>
    [Fact]
    public void ClicDroit_DossierPiege_ArriveEchappeAuTerminal()
    {
        var request = TerminalRequest.Parse(["--terminal", @"C:\dl\p'; calc; #\.", "--", "claude"])
            ?? throw new InvalidOperationException("Request expected");

        var plan = TerminalLaunch.Plan(Pwsh, request) ?? throw new InvalidOperationException("Plan expected");

        Assert.Equal(@"-NoExit -Command ""Set-Location 'C:\dl\p''; calc; #'; claude""", plan.Arguments);
    }

    [Fact]
    public void ClicDroit_DossierPiege_ArriveEchappeAWindowsTerminal()
    {
        var request = TerminalRequest.Parse(["--terminal", @"C:\dl\x;new-tab calc.exe\.", "--", "claude"])
            ?? throw new InvalidOperationException("Request expected");

        var plan = TerminalLaunch.Plan(Wt, request) ?? throw new InvalidOperationException("Plan expected");

        Assert.Contains(@"--startingDirectory ""C:\dl\x\;new-tab calc.exe"" -- claude", plan.Arguments);
        Assert.DoesNotMatch(@"(?<!\\);", plan.Arguments);
    }

    // -- Les autres programmes ne changent pas ----------------------------------

    [Fact]
    public void WezTerm_DossierAvecPointVirguleEtApostrophe_EstInchange()
    {
        var args = TerminalDetectionService.BuildArgs(WezTerm, Folder(@"C:\dl\l'a;b", "claude"));

        Assert.Equal(@"start --new-tab --cwd ""C:\dl\l'a;b"" -- claude", args);
    }

    [Fact]
    public void ProgrammeQuelconque_EstInchange()
    {
        var other = new TerminalInfo { Id = "alacritty", ExePath = @"C:\alacritty.exe", FolderArgs = @"--working-directory ""{folder}""" };

        var args = TerminalDetectionService.BuildArgs(other, Folder(@"C:\dl\l'a;b"));

        Assert.Equal(@"--working-directory ""C:\dl\l'a;b""", args);
    }

    [Theory]
    [InlineData(@"C:\Users\moi\AppData\Local\Microsoft\WindowsApps\wt.exe", TerminalDetectionService.ValueQuoting.WindowsTerminal)]
    [InlineData(@"%LocalAppData%\Microsoft\WindowsApps\WT.EXE", TerminalDetectionService.ValueQuoting.WindowsTerminal)]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24\WindowsTerminal.exe", TerminalDetectionService.ValueQuoting.WindowsTerminal)]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", TerminalDetectionService.ValueQuoting.PowerShell)]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", TerminalDetectionService.ValueQuoting.PowerShell)]
    [InlineData(@"C:\Program Files\WezTerm\wezterm-gui.exe", TerminalDetectionService.ValueQuoting.None)]
    [InlineData("", TerminalDetectionService.ValueQuoting.None)]
    public void LaRegleDEchappementVientDuProgramme(string exePath, TerminalDetectionService.ValueQuoting expected)
    {
        Assert.Equal(expected, TerminalDetectionService.QuotingFor(exePath));
    }
}
