using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class PageActionServiceTests
{
    private static ShortcutEntry E(int page, int row = 0, int col = 0) =>
        new() { Page = page, Row = row, Col = col, Name = "x", Command = "cmd.exe" };

    [Fact]
    public void AddCore_RenvoieIndexSuivant()
    {
        var all = new List<ShortcutEntry> { E(0), E(1) };
        var configs = new List<PageConfig>();
        var r = PageActionService.AddCore(all, configs, "", null);
        Assert.True(r.Ok);
        Assert.Contains(configs, c => c.Index == 2); // page 2 créée
    }

    [Fact]
    public void DeleteCore_SupprimeEtDecale()
    {
        var all = new List<ShortcutEntry> { E(0), E(1), E(2) };
        var configs = new List<PageConfig> { new() { Index = 1 }, new() { Index = 2 } };
        var r = PageActionService.DeleteCore(all, configs, 1);
        Assert.True(r.Ok);
        Assert.Equal(2, all.Count);                       // tuiles de la page 1 supprimées
        Assert.Contains(all, s => s.Page == 1);           // ex-page 2 décalée en 1
        Assert.Single(configs);
        Assert.Equal(1, configs[0].Index);
    }

    [Fact]
    public void UpdateCore_NewIndex_InsertionAvecDecalage()
    {
        // pages 0,1,2 — déplacer 0 vers 2 : 1→0, 2→1, 0→2
        var all = new List<ShortcutEntry> { E(0), E(1), E(2) };
        var configs = new List<PageConfig>();
        var r = PageActionService.UpdateCore(all, configs, 0, false, null, null, newIndex: 2);
        Assert.True(r.Ok);
        Assert.Equal(2, all[0].Page);
        Assert.Equal(0, all[1].Page);
        Assert.Equal(1, all[2].Page);
    }

    [Fact]
    public void UpdateCore_IconNull_RetireLIcone()
    {
        var all = new List<ShortcutEntry> { E(0) };
        var configs = new List<PageConfig> { new() { Index = 0, IconPath = "x.png", IconProfilePath = "icons\\x.png" } };
        var r = PageActionService.UpdateCore(all, configs, 0, iconProvided: true, null, null, null);
        Assert.True(r.Ok);
        Assert.Equal("", configs[0].IconPath);
        Assert.Null(configs[0].IconProfilePath);
    }

    [Fact]
    public void UpdateCore_PageInexistante_Echoue()
    {
        var r = PageActionService.UpdateCore([], [], 5, false, null, null, 0);
        Assert.False(r.Ok);
    }

    [Fact]
    public void UpdateCore_RienAModifier_Echoue()
    {
        var all = new List<ShortcutEntry> { E(0) };
        var configs = new List<PageConfig>();
        var r = PageActionService.UpdateCore(all, configs, 0, iconProvided: false, null, null, newIndex: null);
        Assert.False(r.Ok);
        Assert.Contains("Rien à modifier", r.Error);
    }

    [Fact]
    public void UpdateCore_IconFournie_CreeLaConfigSiAbsente()
    {
        var all = new List<ShortcutEntry> { E(0) };
        var configs = new List<PageConfig>(); // aucune config existante pour la page 0
        var r = PageActionService.UpdateCore(all, configs, 0, iconProvided: true,
            "icon.png", "icons\\icon.png", newIndex: null);
        Assert.True(r.Ok);
        var cfg = Assert.Single(configs);
        Assert.Equal(0, cfg.Index);
        Assert.Equal("icon.png", cfg.IconPath);
        Assert.Equal("icons\\icon.png", cfg.IconProfilePath);
    }

    [Fact]
    public void UpdateCore_NewIndexHorsBornes_Echoue()
    {
        var all = new List<ShortcutEntry> { E(0), E(1) };
        var configs = new List<PageConfig>();
        var r = PageActionService.UpdateCore(all, configs, 0, iconProvided: false, null, null, newIndex: 5);
        Assert.False(r.Ok);
        Assert.Contains("newIndex 5 invalide (pages 0 à 1)", r.Error);
    }

    [Fact]
    public void UpdateCore_NewIndex_RemapSensInverse()
    {
        // pages 0,1,2 — déplacer 2 vers 0 : 2→0, 0→1, 1→2
        var all = new List<ShortcutEntry> { E(0), E(1), E(2) };
        var configs = new List<PageConfig>();
        var r = PageActionService.UpdateCore(all, configs, 2, false, null, null, newIndex: 0);
        Assert.True(r.Ok);
        Assert.Equal(1, all[0].Page);
        Assert.Equal(2, all[1].Page);
        Assert.Equal(0, all[2].Page);
    }
}
