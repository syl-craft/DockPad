using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// La liste des terminaux de terminals.json, sur le modèle des navigateurs : reprise de settings.json,
/// fusion additive de la détection, terminaux proposés (visibles et présents), ordre et identifiants.
/// </summary>
public class TerminalCatalogTests
{
    private static TerminalInfo Detected(string id, string name, string exe) =>
        TerminalDetectionService.Known(id, name, exe);

    private static TerminalEntry Entry(string id, string exe, int order, bool hidden = false) =>
        new() { Id = id, Name = id, ExePath = exe, Order = order, Hidden = hidden, FolderArgs = "f", CommandArgs = "c" };

    // -- Reprise de settings.json ----------------------------------------------

    [Fact]
    public void Reprise_PrendLaDetectionAvecLesArgumentsModifies()
    {
        var entries = TerminalCatalog.Initial(
            [Detected("wt", "Windows Terminal", @"C:\wt.exe"), Detected("pwsh", "PowerShell 7", @"C:\pwsh.exe")],
            legacyTemplates: new Dictionary<string, TerminalSettings> { ["wt"] = new() { FolderArgs = "-d x", CommandArgs = "-d x -- y" } },
            legacyDefinitions: []);

        Assert.Equal(["wt", "pwsh"], entries.Select(e => e.Id));
        Assert.Equal("-d x -- y", entries[0].CommandArgs);
        Assert.Equal([0, 1], entries.Select(e => e.Order));
    }

    [Fact]
    public void Reprise_AjouteLesTerminauxDeclaresSansDoublerUnDetecte()
    {
        var entries = TerminalCatalog.Initial(
            [Detected("wezterm", "WezTerm", @"C:\Program Files\WezTerm\wezterm-gui.exe")],
            legacyTemplates: new Dictionary<string, TerminalSettings>(),
            legacyDefinitions:
            [
                new() { Id = "wezterm", Name = "WezTerm", ExePath = @"C:\Program Files\WezTerm\wezterm-gui.exe" },
                new() { Id = "alacritty", Name = "Alacritty", ExePath = @"C:\alacritty.exe", FolderArgs = "a", CommandArgs = "b" },
                new() { Id = "vide", Name = "Sans programme", ExePath = "" },
            ]);

        Assert.Equal(["wezterm", "alacritty"], entries.Select(e => e.Id));
        Assert.Equal("Alacritty", entries[1].Name);
    }

    // -- Fusion de la détection ------------------------------------------------

    [Fact]
    public void Redetection_AjouteLesNouveauxALaFin()
    {
        var entries = new List<TerminalEntry> { Entry("wt", @"C:\wt.exe", 0) };

        int added = TerminalCatalog.Merge(entries, [Detected("wt", "Windows Terminal", @"C:\wt.exe"), Detected("wezterm", "WezTerm", @"C:\wez.exe")]);

        Assert.Equal(1, added);
        Assert.Equal(["wt", "wezterm"], entries.Select(e => e.Id));
        Assert.Equal(1, entries[1].Order);
        Assert.Equal("WezTerm", entries[1].DetectedName);
    }

    [Fact]
    public void Redetection_NEcraseNiArgumentsNiMasquageNiOrdreNiChemin()
    {
        var wt = Entry("wt", @"D:\portable\wt.exe", order: 3, hidden: true);
        var entries = new List<TerminalEntry> { wt };

        TerminalCatalog.Merge(entries, [Detected("wt", "Windows Terminal", @"C:\wt.exe")]);

        Assert.Equal(("f", "c", true, 3, @"D:\portable\wt.exe"), (wt.FolderArgs, wt.CommandArgs, wt.Hidden, wt.Order, wt.ExePath));
    }

    [Fact]
    public void Redetection_LeNomSuitLaDetectionTantQuIlNEstPasPersonnalise()
    {
        var auto = new TerminalEntry { Id = "wt", Name = "Terminal", DetectedName = "Terminal", ExePath = @"C:\wt.exe" };
        var renamed = new TerminalEntry { Id = "pwsh", Name = "Mon pwsh", DetectedName = "PowerShell", ExePath = @"C:\pwsh.exe", Order = 1 };
        var entries = new List<TerminalEntry> { auto, renamed };

        TerminalCatalog.Merge(entries, [Detected("wt", "Windows Terminal", @"C:\wt.exe"), Detected("pwsh", "PowerShell 7", @"C:\pwsh.exe")]);

        Assert.Equal("Windows Terminal", auto.Name);
        Assert.Equal("Mon pwsh", renamed.Name);
    }

    [Fact]
    public void Redetection_GardeUnTerminalDisparu()
    {
        var entries = new List<TerminalEntry> { Entry("wezterm", @"C:\wez.exe", 0) };

        TerminalCatalog.Merge(entries, []);

        Assert.Single(entries);
    }

    // -- Terminaux proposés ----------------------------------------------------

    [Fact]
    public void Proposes_VisiblesEtPresents_DansLOrdreDeLaListe()
    {
        var entries = new List<TerminalEntry>
        {
            Entry("wezterm", @"C:\wez.exe", 2),
            Entry("wt", @"C:\wt.exe", 0),
            Entry("pwsh", @"C:\pwsh.exe", 1, hidden: true),
            Entry("absent", @"C:\absent.exe", 3),
        };

        var available = TerminalCatalog.Available(entries, exists: path => path != @"C:\absent.exe");

        Assert.Equal(["wt", "wezterm"], available.Select(t => t.Id));
        Assert.Equal("c", available[0].CommandArgs);
    }

    [Fact]
    public void Automatique_PrendLePremierTerminalProposeDansLOrdreDeLaListe()
    {
        var entries = new List<TerminalEntry> { Entry("wezterm", @"C:\wez.exe", 0), Entry("wt", @"C:\wt.exe", 1) };

        var resolved = TerminalDetectionService.Resolve("", TerminalCatalog.Available(entries, exists: _ => true));

        Assert.Equal("wezterm", resolved?.Id);
    }

    [Fact]
    public void TerminalChoisiMasque_SeComporteCommeAutomatique()
    {
        var entries = new List<TerminalEntry> { Entry("wt", @"C:\wt.exe", 0), Entry("wezterm", @"C:\wez.exe", 1, hidden: true) };

        var resolved = TerminalDetectionService.Resolve("wezterm", TerminalCatalog.Available(entries, exists: _ => true));

        Assert.Equal("wt", resolved?.Id);
    }

    // -- Ordre et identifiants -------------------------------------------------

    [Fact]
    public void Deplacer_EchangeAvecLeVoisinEtRenumerote()
    {
        var a = Entry("a", "", 0); var b = Entry("b", "", 5); var c = Entry("c", "", 9);
        var entries = new List<TerminalEntry> { a, b, c };

        TerminalCatalog.Move(entries, c, -1);

        Assert.Equal(["a", "c", "b"], entries.OrderBy(e => e.Order).Select(e => e.Id));
        Assert.Equal([0, 1, 2], entries.OrderBy(e => e.Order).Select(e => e.Order));
    }

    [Fact]
    public void Deplacer_AuBordNeFaitRien()
    {
        var a = Entry("a", "", 0); var b = Entry("b", "", 1);
        var entries = new List<TerminalEntry> { a, b };

        TerminalCatalog.Move(entries, a, -1);

        Assert.Equal(["a", "b"], entries.OrderBy(e => e.Order).Select(e => e.Id));
    }

    [Theory]
    [InlineData("Alacritty", "alacritty")]
    [InlineData("Alacritty Preview", "alacritty-preview")]
    [InlineData("Éditeur_Shell", "editeur-shell")]
    [InlineData("!!!", "terminal")]
    public void NouveauTerminal_IdentifiantTireDuNom(string name, string expected)
    {
        Assert.Equal(expected, TerminalCatalog.NewId(name, []));
    }

    [Fact]
    public void NouveauTerminal_IdentifiantPrisOuReserve_EstSuffixe()
    {
        Assert.Equal("alacritty-2", TerminalCatalog.NewId("Alacritty", ["alacritty"]));
        Assert.Equal("wt-2", TerminalCatalog.NewId("wt", []));
        Assert.Equal("wezterm-2", TerminalCatalog.NewId("WezTerm", []));
    }
}
