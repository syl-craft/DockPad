using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class ShortcutActionServiceTests
{
    private static ShortcutEntry E(int page, int row, int col, string name = "x") =>
        new() { Page = page, Row = row, Col = col, Name = name, Command = "cmd.exe" };

    private static ShortcutAddItem Item(string name = "N", int? page = null, int? row = null, int? col = null) =>
        new() { Name = name, Command = "notepad.exe", Page = page, Row = row, Col = col };

    // ----- AddCore -----

    [Fact]
    public void AddCore_PositionLibre_Ajoute()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0) };
        var r = ShortcutActionService.AddCore(all, [Item("A", 0, 1, 2)]);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 0, Row: 1, Col: 2, Name: "A" });
    }

    [Fact]
    public void AddCore_PositionOccupee_EchoueEnNommantLOccupant()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "VS Code") };
        var r = ShortcutActionService.AddCore(all, [Item("A", 0, 0, 0)]);
        Assert.False(r.Ok);
        Assert.Contains("VS Code", r.Error);
        Assert.Single(all); // aucune mutation
    }

    [Fact]
    public void AddCore_SansPosition_PremiereCaseLibre()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0), E(0, 0, 1) };
        var r = ShortcutActionService.AddCore(all, [Item("A", page: 0)]);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 0, Row: 0, Col: 2, Name: "A" });
    }

    [Fact]
    public void AddCore_LotToutOuRien_UnItemInvalideNAjouteRien()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "occupant") };
        var items = new List<ShortcutAddItem> { Item("OK", 0, 1, 1), Item("KO", 0, 0, 0) };
        var r = ShortcutActionService.AddCore(all, items);
        Assert.False(r.Ok);
        Assert.Single(all);
    }

    [Fact]
    public void AddCore_LotSePlaceSequentiellement()
    {
        var all = new List<ShortcutEntry>();
        var r = ShortcutActionService.AddCore(all, [Item("A", page: 0), Item("B", page: 0)]);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Row: 0, Col: 0, Name: "A" });
        Assert.Contains(all, s => s is { Row: 0, Col: 1, Name: "B" });
    }

    [Fact]
    public void AddCore_PagePleine_Echoue()
    {
        var all = new List<ShortcutEntry>();
        for (int rr = 0; rr < ShortcutActionService.GridRows; rr++)
            for (int cc = 0; cc < ShortcutActionService.GridCols; cc++)
                all.Add(E(0, rr, cc));
        var r = ShortcutActionService.AddCore(all, [Item("A", page: 0)]);
        Assert.False(r.Ok);
        Assert.Contains("pleine", r.Error);
    }

    [Fact]
    public void AddCore_HorsBornes_Echoue()
    {
        var r = ShortcutActionService.AddCore([], [Item("A", 0, 4, 0)]); // row max = 3
        Assert.False(r.Ok);
    }

    [Fact]
    public void AddCore_NomVide_Echoue()
    {
        var r = ShortcutActionService.AddCore([], [Item("", 0, 0, 0)]);
        Assert.False(r.Ok);
    }

    // ----- UpdateCore -----

    [Fact]
    public void UpdateCore_ChampsNonNullsAppliques_AutresConserves()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "Avant") };
        var r = ShortcutActionService.UpdateCore(all, 0, 0, 0, new ShortcutUpdate { Name = "Après" });
        Assert.True(r.Ok);
        Assert.Equal("Après", all[0].Name);
        Assert.Equal("cmd.exe", all[0].Command);
    }

    [Fact]
    public void UpdateCore_TuileIntrouvable_Echoue()
    {
        var r = ShortcutActionService.UpdateCore([], 0, 0, 0, new ShortcutUpdate { Name = "X" });
        Assert.False(r.Ok);
    }

    // ----- MoveCore -----

    [Fact]
    public void MoveCore_SansCible_MemeCaseSiLibre()
    {
        var all = new List<ShortcutEntry> { E(0, 1, 2, "A") };
        var r = ShortcutActionService.MoveCore(all, 0, 1, 2, toPage: 1, null, null);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 1, Row: 1, Col: 2 });
    }

    [Fact]
    public void MoveCore_SansCible_CaseOccupee_PremiereLibre()
    {
        var all = new List<ShortcutEntry> { E(0, 1, 2, "A"), E(1, 1, 2, "B"), E(1, 0, 0, "C") };
        var r = ShortcutActionService.MoveCore(all, 0, 1, 2, toPage: 1, null, null);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 1, Row: 0, Col: 1, Name: "A" });
    }

    [Fact]
    public void MoveCore_CibleExpliciteOccupee_Echoue()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "A"), E(1, 2, 3, "B") };
        var r = ShortcutActionService.MoveCore(all, 0, 0, 0, 1, 2, 3);
        Assert.False(r.Ok);
        Assert.Contains("B", r.Error);
    }

    // ----- DeleteCore / DuplicateCore -----

    [Fact]
    public void DeleteCore_Supprime()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0) };
        var r = ShortcutActionService.DeleteCore(all, 0, 0, 0);
        Assert.True(r.Ok);
        Assert.Empty(all);
    }

    [Fact]
    public void DuplicateCore_CaseLibreLaPlusProche()
    {
        var all = new List<ShortcutEntry> { E(0, 1, 1, "A") };
        var r = ShortcutActionService.DuplicateCore(all, 0, 1, 1);
        Assert.True(r.Ok);
        Assert.Equal(2, all.Count);
        var copy = all[1];
        Assert.Equal(1, Math.Max(Math.Abs(copy.Row - 1), Math.Abs(copy.Col - 1))); // distance Chebyshev 1
    }

    // ----- GetGridCore -----

    [Fact]
    public void GetGridCore_RenvoieTuilesEtCasesLibres()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "A") };
        var r = ShortcutActionService.GetGridCore(all, [], page: 0);
        Assert.True(r.Ok);
        Assert.NotNull(r.Data);
    }
}
