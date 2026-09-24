using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Les tuiles groupées vues par le serveur MCP : création, sous-cases, déplacements. Tout passe par
/// les cœurs purs — la même porte que l'interface, sans fichier ni réseau.
/// </summary>
public class McpGroupTests
{
    private static ShortcutEntry E(int page, int row, int col, string name = "x") =>
        new() { Page = page, Row = row, Col = col, Name = name, Command = "cmd.exe" };

    private static ShortcutEntry Group(int page, int row, int col, TileLayout layout, params ShortcutEntry?[] children)
    {
        var group = new ShortcutEntry { Page = page, Row = row, Col = col, Name = "G", Layout = layout,
                                        Children = children.ToList() };
        TileGroupService.Normalize([group]);
        return group;
    }

    private static ShortcutAddItem Item(string name, int? page = null, int? row = null, int? col = null, int? slot = null) =>
        new() { Name = name, Command = "notepad.exe", Page = page, Row = row, Col = col, Slot = slot };

    // ----- GroupSetCore -----

    [Fact]
    public void GroupSet_CaseVide_CreeUnGroupeNommeEtColore()
    {
        var all = new List<ShortcutEntry>();
        var r = ShortcutActionService.GroupSetCore(all, [], 0, 1, 2, TileLayout.TwoPlusFour, "DockPad", "#f5cc0a");

        Assert.True(r.Ok, r.Error);
        var g = Assert.Single(all);
        Assert.Equal((0, 1, 2), (g.Page, g.Row, g.Col));
        Assert.Equal(TileLayout.TwoPlusFour, g.Layout);
        Assert.Equal("DockPad", g.Name);
        Assert.Equal("#F5CC0A", g.GroupColor);
        Assert.Equal(6, g.Children!.Count);
    }

    [Fact]
    public void GroupSet_TuileSimple_DevientLaPremiereSousCase()
    {
        var tile = E(0, 0, 0, "Dossier");
        var all = new List<ShortcutEntry> { tile };

        Assert.True(ShortcutActionService.GroupSetCore(all, [], 0, 0, 0, TileLayout.Quad, null, null).Ok);

        var g = Assert.Single(all);
        Assert.Same(tile, g.Children![0]);
        Assert.Equal("Dossier", g.Name);
    }

    [Fact]
    public void GroupSet_RenommerUneTuileSimple_EstRefuse()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "Seule") };
        var r = ShortcutActionService.GroupSetCore(all, [], 0, 0, 0, null, "Nouveau", null);

        Assert.False(r.Ok);
        Assert.Equal("Seule", all[0].Name);
    }

    [Fact]
    public void GroupSet_CouleurInvalide_RefuseSansMutation()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad) };
        var r = ShortcutActionService.GroupSetCore(all, [], 0, 0, 0, null, null, "rouge");

        Assert.False(r.Ok);
        Assert.Null(all[0].GroupColor);
    }

    [Fact]
    public void GroupSet_ChangementRefuse_NeLaissePasLaDispositionAppliquee()
    {
        // Tout ou rien : la disposition ne doit pas passer si la couleur, fournie avec, est refusée.
        var all = new List<ShortcutEntry> { E(0, 0, 0) };
        var r = ShortcutActionService.GroupSetCore(all, [], 0, 0, 0, TileLayout.Quad, null, "rouge");

        Assert.False(r.Ok);
        Assert.False(all[0].IsGroup);
    }

    [Fact]
    public void GroupSet_PageInexistante_EstRefusee()
    {
        var r = ShortcutActionService.GroupSetCore([], [], 3, 0, 0, TileLayout.Quad, null, null);
        Assert.False(r.Ok);
        Assert.Contains("inexistante", r.Error);
    }

    [Fact]
    public void GroupSet_SansAucunChamp_EstRefuse()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad) };
        Assert.False(ShortcutActionService.GroupSetCore(all, [], 0, 0, 0, null, null, null).Ok);
    }

    // ----- AddCore avec slot -----

    [Fact]
    public void Add_DansUneSousCaseLibre_PoseLEnfant()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.TwoPlusFour, E(0, 0, 0, "A")) };
        var r = ShortcutActionService.AddCore(all, [], [Item("PS", 0, 0, 0, slot: 5)]);

        Assert.True(r.Ok, r.Error);
        Assert.Single(all);
        Assert.Equal("PS", all[0].Children![5]!.Name);
        Assert.Equal(5, all[0].Children![5]!.Slot);
    }

    [Fact]
    public void Add_SousCaseOccupee_RefuseTouLeLot()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad, E(0, 0, 0, "A")) };
        var r = ShortcutActionService.AddCore(all, [], [Item("B", 0, 1, 0), Item("C", 0, 0, 0, slot: 0)]);

        Assert.False(r.Ok);
        Assert.Contains("« A »", r.Error);
        Assert.Single(all);
    }

    [Fact]
    public void Add_DeuxFoisLaMemeSousCaseDansLeLot_EstRefuse()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad) };
        var r = ShortcutActionService.AddCore(all, [], [Item("B", 0, 0, 0, 1), Item("C", 0, 0, 0, 1)]);

        Assert.False(r.Ok);
        Assert.All(all[0].Children!, c => Assert.Null(c));
    }

    [Theory]
    [InlineData(4)]   // hors de la capacité d'un Quad
    [InlineData(-1)]
    public void Add_SousCaseHorsCapacite_EstRefusee(int slot)
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad) };
        Assert.False(ShortcutActionService.AddCore(all, [], [Item("B", 0, 0, 0, slot)]).Ok);
    }

    [Fact]
    public void Add_SlotSurUneTuileSimple_EstRefuse()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0) };
        var r = ShortcutActionService.AddCore(all, [], [Item("B", 0, 0, 0, 0)]);
        Assert.False(r.Ok);
        Assert.Contains("dockpad_group_set", r.Error);
    }

    [Fact]
    public void Add_SlotSansPosition_EstRefuse()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad) };
        Assert.False(ShortcutActionService.AddCore(all, [], [Item("B", slot: 0)]).Ok);
    }

    // ----- MoveCore avec toSlot -----

    [Fact]
    public void Move_TuileVersSousCaseLibre_LaRetireDeLaGrille()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad), E(0, 2, 3, "T") };
        var r = ShortcutActionService.MoveCore(all, [], 0, 2, 3, 0, 0, 0, toSlot: 2);

        Assert.True(r.Ok, r.Error);
        Assert.Single(all);
        Assert.Equal("T", all[0].Children![2]!.Name);
    }

    [Fact]
    public void Move_SousCaseVersSousCase_DeplaceSansEchanger()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad, E(0, 0, 0, "A"), E(0, 0, 0, "B")) };
        Assert.True(ShortcutActionService.MoveCore(all, [], 0, 0, 0, 0, 0, 0, slot: 0, toSlot: 3).Ok);

        Assert.Null(all[0].Children![0]);
        Assert.Equal("A", all[0].Children![3]!.Name);
    }

    [Fact]
    public void Move_VersSousCaseOccupee_RefuseAuLieuDEchanger()
    {
        // L'interface échange au glisser-déposer ; MCP refuse une case occupée, comme partout ailleurs.
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad, E(0, 0, 0, "A"), E(0, 0, 0, "B")) };
        var r = ShortcutActionService.MoveCore(all, [], 0, 0, 0, 0, 0, 0, slot: 0, toSlot: 1);

        Assert.False(r.Ok);
        Assert.Equal("A", all[0].Children![0]!.Name);
    }

    [Fact]
    public void Move_SousCaseVersCaseDeGrille_SortLEnfantDuGroupe()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad, E(0, 0, 0, "A")) };
        Assert.True(ShortcutActionService.MoveCore(all, [], 0, 0, 0, 0, 1, 1, slot: 0).Ok);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s is { Name: "A", Row: 1, Col: 1, Slot: null });
    }

    [Fact]
    public void Move_GroupeVersSousCase_EstRefuse()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad), Group(0, 0, 1, TileLayout.Quad) };
        Assert.False(ShortcutActionService.MoveCore(all, [], 0, 0, 0, 0, 0, 1, toSlot: 0).Ok);
    }

    // ----- UpdateCore / DeleteCore avec slot -----

    [Fact]
    public void Update_SousCase_ModifieLEnfant()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad, null, E(0, 0, 0, "A")) };
        Assert.True(ShortcutActionService.UpdateCore(all, 0, 0, 0, new ShortcutUpdate { Name = "B" }, slot: 1).Ok);
        Assert.Equal("B", all[0].Children![1]!.Name);
    }

    [Fact]
    public void Delete_SousCase_VideLaSeuleSousCase()
    {
        var all = new List<ShortcutEntry> { Group(0, 0, 0, TileLayout.Quad, E(0, 0, 0, "A"), E(0, 0, 0, "B")) };
        Assert.True(ShortcutActionService.DeleteCore(all, 0, 0, 0, slot: 1).Ok);

        Assert.Single(all);
        Assert.Equal("A", all[0].Children![0]!.Name);
        Assert.Null(all[0].Children![1]);
    }

    // ----- GetGridCore -----

    [Fact]
    public void GridGet_ExposeCouleurEtSousCasesLibres()
    {
        var g = Group(0, 0, 0, TileLayout.Quad, E(0, 0, 0, "A"), null, E(0, 0, 0, "C"));
        g.GroupColor = "#112233";
        var r = ShortcutActionService.GetGridCore([g], [], null);

        var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
        Assert.Contains("\"groupColor\":\"#112233\"", json);
        Assert.Contains("\"freeSlots\":[1,3]", json);
    }
}
