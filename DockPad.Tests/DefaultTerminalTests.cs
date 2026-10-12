using System.IO;
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
        TerminalDetectionService.Known(TerminalDetectionService.WindowsTerminalId, "WindowsTerminalId", @"C:\wt.exe");
    private static readonly TerminalInfo Pwsh =
        TerminalDetectionService.Known(TerminalDetectionService.PowerShell7Id, "PowerShell7Id", @"C:\pwsh.exe");
    private static readonly TerminalInfo WindowsPowerShell =
        TerminalDetectionService.Known(TerminalDetectionService.WindowsPowerShellId, "WindowsPowerShellId", @"C:\powershell.exe");

    // -- Quel terminal ---------------------------------------------------------

    [Fact]
    public void Automatique_PrendLePremierDansLOrdreDeDetection()
    {
        var resolved = TerminalDetectionService.Resolve("", custom: null, [Wt, Pwsh, WindowsPowerShell]);

        Assert.Same(Wt, resolved);
    }

    [Fact]
    public void Automatique_SansWindowsTerminal_PrendPowerShell7()
    {
        var resolved = TerminalDetectionService.Resolve("", custom: null, [Pwsh, WindowsPowerShell]);

        Assert.Same(Pwsh, resolved);
    }

    [Fact]
    public void TerminalChoisi_EstRetenuSIlEstInstalle()
    {
        var resolved = TerminalDetectionService.Resolve("powershell", custom: null, [Wt, Pwsh, WindowsPowerShell]);

        Assert.Same(WindowsPowerShell, resolved);
    }

    [Theory]
    [InlineData("pwsh")]      // choisi puis désinstallé
    [InlineData("cmd")]       // retiré de la liste, écrit par une version antérieure
    [InlineData("inconnu")]   // écrit par une version plus récente
    public void TerminalChoisiAbsent_SeComporteCommeAutomatique(string setting)
    {
        var resolved = TerminalDetectionService.Resolve(setting, custom: null, [Wt, WindowsPowerShell]);

        Assert.Same(Wt, resolved);
    }

    [Fact]
    public void AucunTerminalInstalle_NeRendRien()
    {
        Assert.Null(TerminalDetectionService.Resolve("", custom: null, []));
    }

    // -- Programme ajouté et modèles d'arguments -------------------------------

    private static TerminalSettings Custom(string exe, string folderArgs, string commandArgs) =>
        new() { ExePath = exe, FolderArgs = folderArgs, CommandArgs = commandArgs };

    [Fact]
    public void ProgrammeAjoute_SansCommande_SuitLeModeleDuDossier()
    {
        // MXterm Pro ne prend qu'un dossier.
        var mxterm = TerminalDetectionService.CustomTerminal(Custom(@"C:\MXterm Pro\mxterm.exe", @"""{folder}""", @"""{folder}"""))
            ?? throw new InvalidOperationException("programme vide");

        var args = TerminalDetectionService.BuildArgs(mxterm, new TerminalConfig { StartingDirectory = @"C:\dev\Demo" });

        Assert.Equal(TerminalDetectionService.CustomId, mxterm.Id);
        Assert.Equal(@"""C:\dev\Demo""", args);
    }

    [Fact]
    public void ProgrammeAjoute_AvecCommande_SuitLeModeleDeLaCommande()
    {
        var alacritty = TerminalDetectionService.CustomTerminal(Custom(@"C:\alacritty.exe",
            @"--working-directory ""{folder}""", @"--working-directory ""{folder}"" -e {command}"))
            ?? throw new InvalidOperationException("programme vide");
        var cfg = new TerminalConfig { StartingDirectory = "%V", RunCommand = @"""C:\codex.exe""" };

        Assert.Equal(@"--working-directory ""%V"" -e ""C:\codex.exe""", TerminalDetectionService.BuildArgs(alacritty, cfg));
    }

    [Fact]
    public void ModeleModifie_RemplaceCeuxDeDockPad()
    {
        var wt = TerminalDetectionService.WithTemplates(Wt,
            new TerminalSettings { FolderArgs = @"-d ""{folder}""", CommandArgs = @"-d ""{folder}"" {command}" });

        var args = TerminalDetectionService.BuildArgs(wt, new TerminalConfig { StartingDirectory = @"C:\dev", RunCommand = "claude" });

        Assert.Equal(@"-d ""C:\dev"" claude", args);
    }

    [Theory]
    [InlineData(@"D:\Outils\wt.exe", "wt")]
    [InlineData(@"C:\Program Files\PowerShell\7-preview\PWSH.EXE", "pwsh")]
    [InlineData(@"D:\portable\powershell.exe", "powershell")]
    [InlineData(@"C:\MXterm Pro\mxterm.exe", "custom")]
    public void ProgrammeConnu_ReconnuParLeNomDeLExecutable(string path, string expectedId)
    {
        Assert.Equal(expectedId, TerminalDetectionService.KnownIdOf(path));
    }

    [Fact]
    public void CheminAvecVariableDEnvironnement_EstTrouveEtGardeTelQuel()
    {
        // Gardée non développée, la variable suit l'utilisateur dans une clé de registre machine.
        string file = Path.Combine(Path.GetTempPath(), $"dockpad-term-{Guid.NewGuid():N}.exe");
        File.WriteAllText(file, "");
        try
        {
            string withVariable = Path.Combine("%TEMP%", Path.GetFileName(file));

            var custom = TerminalDetectionService.ExistingCustomTerminal(Custom(withVariable, @"""{folder}""", @"""{folder}"""))
                ?? throw new InvalidOperationException("programme introuvable");

            Assert.Equal(withVariable, custom.ExePath);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ProgrammeIntrouvable_NeRendRien()
    {
        Assert.Null(TerminalDetectionService.ExistingCustomTerminal(Custom(@"C:\nulle-part\absent.exe", "", "")));
    }

    [Fact]
    public void ProgrammeVide_NeRendRien()
    {
        Assert.Null(TerminalDetectionService.CustomTerminal(Custom("", @"""{folder}""", "")));
    }

    [Fact]
    public void ProgrammeAjoute_EstRetenuQuandIlEstChoisi()
    {
        var custom = TerminalDetectionService.CustomTerminal(Custom(@"D:\pwsh-preview\pwsh.exe", "", ""));

        var resolved = TerminalDetectionService.Resolve(TerminalDetectionService.CustomId, custom, [Wt, Pwsh]);

        Assert.Same(custom, resolved);
    }

    [Fact]
    public void ProgrammeAjouteIntrouvable_SeComporteCommeAutomatique()
    {
        var resolved = TerminalDetectionService.Resolve(TerminalDetectionService.CustomId, custom: null, [Wt, Pwsh]);

        Assert.Same(Wt, resolved);
    }

    [Fact]
    public void WindowsTerminalPortable_EstTrouveDansLePath()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"dockpad-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string wt = Path.Combine(dir, "wt.exe");
            File.WriteAllText(wt, "");

            var found = TerminalDetectionService.FindInPath("wt.exe", $@"C:\nulle-part;{dir}");

            Assert.Equal(wt, found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RepliDesPredefinis_OuvreLeDossierEtLanceLaCommande()
    {
        // Sans terminal détecté, le prédéfini Claude doit encore ouvrir le dossier et lancer claude.
        var cmd = PresetService.TerminalCommand(PresetService.FallbackTerminal, "claude");

        Assert.Equal(@"""powershell.exe"" -NoExit -Command ""Set-Location '%V'; claude""", cmd);
    }

    // -- Nom de fenêtre (wt -w) ------------------------------------------------

    [Fact]
    public void NomDeTuile_RegroupeLesOngletsDansUneFenetreNommee()
    {
        var wt = TerminalDetectionService.WithTemplates(Wt, new TerminalSettings
        {
            FolderArgs  = @"-w ""{name}"" new-tab --startingDirectory ""{folder}""",
            CommandArgs = @"-w ""{name}"" new-tab --startingDirectory ""{folder}"" -- {command}",
        });
        var cfg = new TerminalConfig { StartingDirectory = @"C:\dev\Shope", RunCommand = "claude" };

        var args = TerminalDetectionService.BuildArgs(wt, cfg, "Shope - specs");

        Assert.Equal(@"-w ""Shope - specs"" new-tab --startingDirectory ""C:\dev\Shope"" -- claude", args);
    }

    [Theory]
    [InlineData(@"C:\dev\DockPad", "DockPad")]
    [InlineData(@"C:\dev\DockPad\", "DockPad")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("%V", "%V")]
    public void NomDuDossier_EstSonDernierSegment(string folder, string expected)
    {
        Assert.Equal(expected, TerminalDetectionService.FolderName(folder));
    }

    [Fact]
    public void SansNomDeTuile_NameVautLeNomDuDossier()
    {
        string args = TerminalDetectionService.Expand(@"-w ""{name}""", @"C:\dev\DockPad", command: "");

        Assert.Equal(@"-w ""DockPad""", args);
    }

    [Fact]
    public void CouleurChoisieParLaTuile_RemplaceCelleDuDossier()
    {
        var args = TerminalDetectionService.BuildArgs(Wt,
            new TerminalConfig { StartingDirectory = @"C:\dev\DockPad", RunCommand = "claude", TabColor = "#FF8800" }, "DockPad");

        Assert.Contains("--tabColor #FF8800", args);
    }

    [Theory]
    [InlineData("rouge")]
    [InlineData("#12345")]
    [InlineData("")]
    public void CouleurInvalideOuVide_PrendCelleDuDossier(string color)
    {
        var args = TerminalDetectionService.BuildArgs(Wt,
            new TerminalConfig { StartingDirectory = @"C:\dev\DockPad", TabColor = color });

        Assert.Contains($"--tabColor {TerminalDetectionService.TabColor("DockPad")}", args);
    }

    [Theory]
    [InlineData("#F80", true)]
    [InlineData("#ff8800", true)]
    [InlineData("ff8800", false)]
    [InlineData("#GG0000", false)]
    public void CouleurDOnglet_FormatsAcceptesParWindowsTerminal(string color, bool valid)
    {
        Assert.Equal(valid, TerminalDetectionService.IsTabColor(color));
    }

    [Fact]
    public void CouleurDOnglet_StablePourUnMemeDossier_SansEgardALaCasse()
    {
        Assert.Equal(TerminalDetectionService.TabColor("DockPad"), TerminalDetectionService.TabColor("dockpad"));
        Assert.Matches("^#[0-9A-F]{6}$", TerminalDetectionService.TabColor("DockPad"));
    }

    [Fact]
    public void CouleurDOnglet_VarieDUnProjetALAutre()
    {
        string[] projects = ["DockPad", "Shope-stage", "Tandoor", "AudioBox-Pro", "Backup-All-2", "Qnap"];

        Assert.True(projects.Select(TerminalDetectionService.TabColor).Distinct().Count() > 1);
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

        Assert.Equal($@"-w ""dev"" new-tab --tabColor {TerminalDetectionService.TabColor("dev")} --startingDirectory ""C:\dev"" -- claude --remote-control ""Train de Noël""", args);
    }
}
