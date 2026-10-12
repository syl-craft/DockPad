using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// <c>DockPad.exe --terminal "%V\." -- commande</c>, la ligne des prédéfinis du clic droit : lecture du
/// dossier (racine de lecteur comprise), recomposition de la commande avec ses guillemets, transport
/// par le tube, et commande obtenue — la même que celle d'une tuile sur ce dossier.
/// </summary>
public class TerminalLaunchTests
{
    private static readonly TerminalInfo Wt =
        TerminalDetectionService.Known(TerminalDetectionService.WindowsTerminalId, "Windows Terminal", @"C:\wt.exe");

    [Fact]
    public void Dossier_PerdSonPointFinal()
    {
        var request = TerminalRequest.Parse(["--terminal", @"C:\dev\DockPad\.", "--", "claude"]);

        Assert.Equal(new TerminalRequest.Request(@"C:\dev\DockPad", "claude"), request);
    }

    [Fact]
    public void RacineDeLecteur_GardeSaBarre()
    {
        var request = TerminalRequest.Parse(["--terminal", @"D:\.", "--", "claude"]);

        Assert.Equal(@"D:\", request?.Folder);
    }

    [Fact]
    public void Commande_RetrouveSesGuillemets()
    {
        // Windows a découpé « "C:\Program Files\codex.exe" » et « --remote-control "Train de Noël" » :
        // les recoller sans guillemets couperait le chemin et le titre à l'espace.
        var request = TerminalRequest.Parse(
            ["--terminal", @"C:\dev\.", "--", @"C:\Program Files\OpenAI\codex.exe", "--remote-control", "Train de Noël"]);

        Assert.Equal(@"""C:\Program Files\OpenAI\codex.exe"" --remote-control ""Train de Noël""", request?.Command);
    }

    [Fact]
    public void SansCommande_OuvreSeulementLeDossier()
    {
        var request = TerminalRequest.Parse(["--terminal", @"C:\dev\."]);

        Assert.Equal("", request?.Command);
    }

    [Fact]
    public void AutreLancement_NEstPasUneDemandeDeTerminal()
    {
        Assert.Null(TerminalRequest.Parse(["--url", "https://claude.ai"]));
        Assert.Null(TerminalRequest.Parse(["--terminal"]));
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("deux mots", "\"deux mots\"")]
    [InlineData("", "\"\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"C:\fin\", "C:\\fin\\")]
    [InlineData(@"C:\a b\", "\"C:\\a b\\\\\"")]
    public void Argument_GuillemetsSelonLesReglesDeWindows(string arg, string expected)
    {
        Assert.Equal(expected, TerminalRequest.JoinArguments([arg]));
    }

    [Fact]
    public void Tube_TransporteDossierEtCommande()
    {
        var request = new TerminalRequest.Request(@"C:\dev\DockPad", @"claude --remote-control ""x y""");

        Assert.Equal(request, TerminalRequest.Decode(TerminalRequest.Encode(request)));
    }

    [Fact]
    public void Commande_IdentiqueACelleDUneTuileSurLeMemeDossier()
    {
        var request = new TerminalRequest.Request(@"C:\dev\DockPad", "claude");
        var tile = new ShortcutEntry
        {
            Name = "DockPad", Type = ShortcutType.OpenTerminal, Command = @"C:\dev\DockPad",
            Terminal = new TerminalConfig { StartingDirectory = @"C:\dev\DockPad", RunCommand = "claude" },
        };

        var fromPreset = TerminalLaunch.Plan(Wt, request);
        var fromTile = ShortcutLauncher.Plan(tile, Wt);

        Assert.Equal(fromTile, fromPreset);
        Assert.Contains(@"-w ""DockPad""", fromPreset?.Arguments);
        Assert.Contains($"--tabColor {TerminalDetectionService.TabColor("DockPad")}", fromPreset?.Arguments);
    }

    [Fact]
    public void SansTerminal_NaPasDePlan()
    {
        Assert.Null(TerminalLaunch.Plan(null, new TerminalRequest.Request(@"C:\dev", "")));
    }

    [Fact]
    public void Predefini_LanceDockPadAvecLeDossierEtLaCommande()
    {
        var command = PresetService.LauncherCommand(@"C:\DockPad\DockPad.exe", "claude --enable-auto-mode");

        Assert.Equal(@"""C:\DockPad\DockPad.exe"" --terminal ""%V\."" -- claude --enable-auto-mode", command);
    }

    [Fact]
    public void PredefiniSansCommande_NePortePasDeSeparateur()
    {
        Assert.Equal(@"""C:\DockPad\DockPad.exe"" --terminal ""%V\.""", PresetService.LauncherCommand(@"C:\DockPad\DockPad.exe", ""));
    }
}
