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
        // Windows a découpé « "C:\Program Files\codex.exe" » et « --remote-control "Projet de démo" » :
        // les recoller sans guillemets couperait le chemin et le titre à l'espace.
        var request = TerminalRequest.Parse(
            ["--terminal", @"C:\dev\.", "--", @"C:\Program Files\OpenAI\codex.exe", "--remote-control", "Projet de démo"]);

        Assert.Equal(@"""C:\Program Files\OpenAI\codex.exe"" --remote-control ""Projet de démo""", request?.Command);
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

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("a&b", "\"a&b\"")]
    [InlineData("a|b", "\"a|b\"")]
    [InlineData("--model=gpt", "--model=gpt")]
    [InlineData("démo", "démo")]
    public void Argument_SeparateurDeShell_RestEntreGuillemets(string arg, string expected)
    {
        Assert.Equal(expected, TerminalRequest.JoinArguments([arg]));
    }

    [Fact]
    public void FormeDuTube_RedonneLaMemeDemande()
    {
        var request = new TerminalRequest.Request(@"D:\", @"""C:\Program Files\codex.exe"" --x ""a;b""");

        Assert.Equal(request, TerminalRequest.Parse(TerminalRequest.ToArgs(request)));
    }

    [Fact]
    public void RacineDeLecteur_NEchappePasLeGuillemetFermant()
    {
        var plan = TerminalLaunch.Plan(Wt, new TerminalRequest.Request(@"D:\", "claude"));

        Assert.Equal($@"-w ""D:"" new-tab --tabColor {TerminalDetectionService.TabColor("D:")} --startingDirectory ""D:\\"" -- claude", plan?.Arguments);
    }

    [Fact]
    public void InstanceQuiRepond_RecoitLaDemande_SansLancementLocal()
    {
        string? sent = null;
        bool launched = false;

        bool handled = TerminalLaunch.TryHandle(["--terminal", @"C:\dev\.", "--", "claude"],
            instanceIsRunning: () => true, send: line => { sent = line; return true; }, launch: _ => launched = true);

        Assert.True(handled);
        Assert.Equal(TerminalRequest.Encode(new(@"C:\dev", "claude")), sent);
        Assert.False(launched);
    }

    [Fact]
    public void InstanceMuette_LancementLocal()
    {
        TerminalRequest.Request? launched = null;

        TerminalLaunch.TryHandle(["--terminal", @"C:\dev\."],
            instanceIsRunning: () => true, send: _ => false, launch: request => launched = request);

        Assert.Equal(new TerminalRequest.Request(@"C:\dev", ""), launched);
    }

    [Fact]
    public void SansInstance_LeTubeNEstPasEssaye()
    {
        bool tried = false;
        bool launched = false;

        TerminalLaunch.TryHandle(["--terminal", @"C:\dev\."],
            instanceIsRunning: () => false, send: _ => tried = true, launch: _ => launched = true);

        Assert.False(tried);
        Assert.True(launched);
    }

    [Fact]
    public void AutreLancement_NiTubeNiLancement()
    {
        bool touched = false;

        bool handled = TerminalLaunch.TryHandle(["--url", "https://claude.ai"],
            instanceIsRunning: () => touched = true, send: _ => touched = true, launch: _ => touched = true);

        Assert.False(handled);
        Assert.False(touched);
    }
}
