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

    private static AppSettings Legacy(string defaultTerminal = "",
        Dictionary<string, TerminalSettings>? templates = null, List<TerminalDefinition>? definitions = null,
        TerminalSettings? custom = null) => new()
    {
        DefaultTerminal   = defaultTerminal,
        TerminalTemplates = templates ?? new(),
        Terminals         = definitions ?? new(),
        CustomTerminal    = custom ?? new(),
    };

    [Fact]
    public void Reprise_PrendLaDetectionAvecLesArgumentsModifies()
    {
        var migration = TerminalCatalog.Migrate(
            [Detected("wt", "Windows Terminal", @"C:\wt.exe"), Detected("pwsh", "PowerShell 7", @"C:\pwsh.exe")],
            Legacy(templates: new() { ["wt"] = new() { FolderArgs = "-d x", CommandArgs = "-d x -- y" } }));

        Assert.Equal(["wt", "pwsh"], migration.Terminals.Select(e => e.Id));
        Assert.Equal("-d x -- y", migration.Terminals[0].CommandArgs);
        Assert.Equal([0, 1], migration.Terminals.Select(e => e.Order));
        Assert.Empty(migration.PendingTemplates);
    }

    [Fact]
    public void Reprise_GardeLesArgumentsDUnTerminalAbsent_EtLaRedetectionLesReprend()
    {
        var migration = TerminalCatalog.Migrate([Detected("wt", "Windows Terminal", @"C:\wt.exe")],
            Legacy(templates: new() { ["pwsh"] = new() { FolderArgs = "-p", CommandArgs = "-p -c" } }));

        Assert.True(migration.PendingTemplates.ContainsKey("pwsh"));

        TerminalCatalog.Merge(migration.Terminals, [Detected("pwsh", "PowerShell 7", @"C:\pwsh.exe")], migration.PendingTemplates);

        Assert.Equal("-p -c", migration.Terminals.Where(e => e.Id == "pwsh").Single().CommandArgs);
        Assert.Empty(migration.PendingTemplates);
    }

    [Fact]
    public void Reprise_UneDeclarationAuProgrammeDUnDetecte_LuiDonneSonNomEtSesArguments()
    {
        var migration = TerminalCatalog.Migrate(
            [Detected("wezterm", "WezTerm", @"C:\Program Files\WezTerm\wezterm-gui.exe")],
            Legacy(defaultTerminal: "wez", definitions:
            [
                new() { Id = "wez", Name = "Mon WezTerm", ExePath = @"C:\Program Files\WezTerm\wezterm-gui.exe",
                        FolderArgs = "start --cwd \"{folder}\"", CommandArgs = "start --cwd \"{folder}\" -- {command}" },
            ]));

        var wezterm = Assert.Single(migration.Terminals);
        Assert.Equal(("wezterm", "Mon WezTerm", "start --cwd \"{folder}\" -- {command}"), (wezterm.Id, wezterm.Name, wezterm.CommandArgs));
        Assert.Equal("wezterm", migration.DefaultTerminal);
    }

    [Fact]
    public void Reprise_GardeLIdentifiantDUneDeclaration_EtLeTerminalParDefautQuiLaVise()
    {
        var migration = TerminalCatalog.Migrate([],
            Legacy(defaultTerminal: "mxterm", definitions:
            [
                new() { Id = "mxterm", Name = "MXterm Pro", ExePath = @"C:\MXterm\mxterm.exe", FolderArgs = "\"{folder}\"" },
                new() { Id = "", Name = "Sans programme", ExePath = "" },
            ]));

        var mxterm = Assert.Single(migration.Terminals);
        Assert.Equal(("mxterm", "MXterm Pro"), (mxterm.Id, mxterm.Name));
        Assert.Equal("mxterm", migration.DefaultTerminal);
    }

    [Fact]
    public void Reprise_IdentifiantPris_EnChoisitUnAutreEtLeTerminalParDefautSuit()
    {
        var migration = TerminalCatalog.Migrate([Detected("wt", "Windows Terminal", @"C:\wt.exe")],
            Legacy(defaultTerminal: "wt", definitions: [new() { Id = "wt", Name = "Mon wt", ExePath = @"D:\portable\wt.exe" }]));

        Assert.Equal(["wt", "mon-wt"], migration.Terminals.Select(e => e.Id));
        Assert.Equal("mon-wt", migration.DefaultTerminal);
    }

    [Fact]
    public void Reprise_LAncienProgrammeUniqueDevientUnTerminalDeLaListe()
    {
        var migration = TerminalCatalog.Migrate([],
            Legacy(defaultTerminal: "custom",
                   custom: new() { ExePath = @"C:\Tools\alacritty.exe", FolderArgs = "a", CommandArgs = "b" }));

        var custom = Assert.Single(migration.Terminals);
        Assert.Equal(("custom", "alacritty", "b"), (custom.Id, custom.Name, custom.CommandArgs));
        Assert.Equal("custom", migration.DefaultTerminal);
    }

    // -- Fusion de la détection ------------------------------------------------

    [Fact]
    public void Redetection_AjouteLesNouveauxALaFin()
    {
        var entries = new List<TerminalEntry> { Entry("wt", @"C:\wt.exe", 0) };

        int added = TerminalCatalog.Merge(entries, [Detected("wt", "Windows Terminal", @"C:\wt.exe"), Detected("wezterm", "WezTerm", @"C:\wez.exe")], new());

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

        TerminalCatalog.Merge(entries, [Detected("wt", "Windows Terminal", @"C:\wt.exe")], new());

        Assert.Equal(("f", "c", true, 3, @"D:\portable\wt.exe"), (wt.FolderArgs, wt.CommandArgs, wt.Hidden, wt.Order, wt.ExePath));
    }

    [Fact]
    public void Redetection_LeNomSuitLaDetectionTantQuIlNEstPasPersonnalise()
    {
        var auto = new TerminalEntry { Id = "wt", Name = "Terminal", DetectedName = "Terminal", ExePath = @"C:\wt.exe" };
        var renamed = new TerminalEntry { Id = "pwsh", Name = "Mon pwsh", DetectedName = "PowerShell", ExePath = @"C:\pwsh.exe", Order = 1 };
        var entries = new List<TerminalEntry> { auto, renamed };

        TerminalCatalog.Merge(entries, [Detected("wt", "Windows Terminal", @"C:\wt.exe"), Detected("pwsh", "PowerShell 7", @"C:\pwsh.exe")], new());

        Assert.Equal("Windows Terminal", auto.Name);
        Assert.Equal("Mon pwsh", renamed.Name);
    }

    [Fact]
    public void Redetection_GardeUnTerminalDisparu()
    {
        var entries = new List<TerminalEntry> { Entry("wezterm", @"C:\wez.exe", 0) };

        TerminalCatalog.Merge(entries, [], new());

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
