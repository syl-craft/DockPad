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
        var r = ShortcutActionService.AddCore(all, [], [Item("A", 0, 1, 2)]);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 0, Row: 1, Col: 2, Name: "A" });
    }

    [Fact]
    public void AddCore_PositionOccupee_EchoueEnNommantLOccupant()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "VS Code") };
        var r = ShortcutActionService.AddCore(all, [], [Item("A", 0, 0, 0)]);
        Assert.False(r.Ok);
        Assert.Contains("VS Code", r.Error);
        Assert.Single(all); // aucune mutation
    }

    [Fact]
    public void AddCore_SansPosition_PremiereCaseLibre()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0), E(0, 0, 1) };
        var r = ShortcutActionService.AddCore(all, [], [Item("A", page: 0)]);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 0, Row: 0, Col: 2, Name: "A" });
    }

    [Fact]
    public void AddCore_LotToutOuRien_UnItemInvalideNAjouteRien()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "occupant") };
        var items = new List<ShortcutAddItem> { Item("OK", 0, 1, 1), Item("KO", 0, 0, 0) };
        var r = ShortcutActionService.AddCore(all, [], items);
        Assert.False(r.Ok);
        Assert.Single(all);
    }

    [Fact]
    public void AddCore_LotSePlaceSequentiellement()
    {
        var all = new List<ShortcutEntry>();
        var r = ShortcutActionService.AddCore(all, [], [Item("A", page: 0), Item("B", page: 0)]);
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
        var r = ShortcutActionService.AddCore(all, [], [Item("A", page: 0)]);
        Assert.False(r.Ok);
        Assert.Contains("pleine", r.Error);
    }

    [Fact]
    public void AddCore_HorsBornes_Echoue()
    {
        var r = ShortcutActionService.AddCore([], [], [Item("A", 0, 4, 0)]); // row max = 3
        Assert.False(r.Ok);
    }

    [Fact]
    public void AddCore_NomVide_Echoue()
    {
        var r = ShortcutActionService.AddCore([], [], [Item("", 0, 0, 0)]);
        Assert.False(r.Ok);
    }

    [Fact]
    public void AddCore_PageInexistante_Echoue()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0) };
        var r = ShortcutActionService.AddCore(all, [], [Item("A", page: 5)]);
        Assert.False(r.Ok);
        Assert.Contains("inexistante", r.Error);
    }

    [Fact]
    public void AddCore_PageExistanteViaConfigSeule_Reussit()
    {
        var all = new List<ShortcutEntry>();
        var configs = new List<PageConfig> { new() { Index = 1 } };
        var r = ShortcutActionService.AddCore(all, configs, [Item("A", page: 1)]);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 1, Row: 0, Col: 0, Name: "A" });
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
        var configs = new List<PageConfig> { new() { Index = 1 } };
        var r = ShortcutActionService.MoveCore(all, configs, 0, 1, 2, toPage: 1, null, null);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 1, Row: 1, Col: 2 });
    }

    [Fact]
    public void MoveCore_SansCible_CaseOccupee_PremiereLibre()
    {
        var all = new List<ShortcutEntry> { E(0, 1, 2, "A"), E(1, 1, 2, "B"), E(1, 0, 0, "C") };
        var r = ShortcutActionService.MoveCore(all, [], 0, 1, 2, toPage: 1, null, null);
        Assert.True(r.Ok);
        Assert.Contains(all, s => s is { Page: 1, Row: 0, Col: 1, Name: "A" });
    }

    [Fact]
    public void MoveCore_CibleExpliciteOccupee_Echoue()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "A"), E(1, 2, 3, "B") };
        var r = ShortcutActionService.MoveCore(all, [], 0, 0, 0, 1, 2, 3);
        Assert.False(r.Ok);
        Assert.Contains("B", r.Error);
    }

    [Fact]
    public void MoveCore_PageCibleInexistante_Echoue()
    {
        var all = new List<ShortcutEntry> { E(0, 0, 0, "A") };
        var r = ShortcutActionService.MoveCore(all, [], 0, 0, 0, toPage: 7, null, null);
        Assert.False(r.Ok);
        Assert.Contains("inexistante", r.Error);
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

    [Fact]
    public void DuplicateCore_CopieToutesLesConfigsProcessSwitch()
    {
        var source = E(0, 1, 1, "A");
        source.Type = ShortcutType.SwitchToProcess;
        source.ProcessSwitch = new ProcessSwitchConfig
        {
            SearchMode = ProcessSearchMode.ByWindowTitle,
            ProcessName = "Calculatrice",
            Executable = @"C:\calc.exe",
            Parameters = "--foo",
        };
        var all = new List<ShortcutEntry> { source };
        var r = ShortcutActionService.DuplicateCore(all, 0, 1, 1);
        Assert.True(r.Ok);
        var copy = all[1].ProcessSwitch;
        Assert.NotNull(copy);
        Assert.Equal(ProcessSearchMode.ByWindowTitle, copy!.SearchMode);
        Assert.Equal("Calculatrice", copy.ProcessName);
        Assert.Equal(@"C:\calc.exe", copy.Executable);
        Assert.Equal("--foo", copy.Parameters);
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

    // ── FirstFreeSlot : la case libre, dans une grille entière ────────────────

    private static List<ShortcutEntry> Full(int page)
    {
        var list = new List<ShortcutEntry>();
        for (int r = 0; r < ShortcutActionService.GridRows; r++)
            for (int c = 0; c < ShortcutActionService.GridCols; c++)
                list.Add(E(page, r, c));
        return list;
    }

    [Fact]
    public void FirstFreeSlot_GrilleVide_PremiereCase()
    {
        var slot = ShortcutActionService.FirstFreeSlot([], []);

        Assert.Equal((0, 0, 0), (slot.Page, slot.Row, slot.Col));
        Assert.False(slot.NeedsNewPage);
    }

    [Fact]
    public void FirstFreeSlot_SuitLOrdreDeLecture()
    {
        var slot = ShortcutActionService.FirstFreeSlot([E(0, 0, 0), E(0, 0, 1)], []);

        Assert.Equal((0, 0, 2), (slot.Page, slot.Row, slot.Col));
    }

    [Fact]
    public void FirstFreeSlot_ReboucheUnTrou()
    {
        var all = Full(0);
        all.RemoveAll(s => s is { Row: 2, Col: 3 });

        var slot = ShortcutActionService.FirstFreeSlot(all, []);

        Assert.Equal((0, 2, 3), (slot.Page, slot.Row, slot.Col));
    }

    [Fact]
    public void FirstFreeSlot_PagePleine_PasseALaSuivante()
    {
        var all = Full(0);
        all.Add(E(1, 0, 0));

        var slot = ShortcutActionService.FirstFreeSlot(all, []);

        Assert.Equal((1, 0, 1), (slot.Page, slot.Row, slot.Col));
        Assert.False(slot.NeedsNewPage);
    }

    [Fact]
    public void FirstFreeSlot_PageDeclareeVide_YAtterrit()
    {
        var slot = ShortcutActionService.FirstFreeSlot(Full(0), [new PageConfig { Index = 1 }]);

        Assert.Equal((1, 0, 0), (slot.Page, slot.Row, slot.Col));
        Assert.False(slot.NeedsNewPage);
    }

    [Fact]
    public void FirstFreeSlot_ToutPlein_DemandeUnePageNeuve()
    {
        var all = Full(0);
        all.AddRange(Full(1));

        var slot = ShortcutActionService.FirstFreeSlot(all, []);

        Assert.Equal((2, 0, 0), (slot.Page, slot.Row, slot.Col));
        Assert.True(slot.NeedsNewPage);
    }

    // ── TransferCore : d'une grille à l'autre ─────────────────────────────────

    [Fact]
    public void TransferCore_LaTuileQuitteLaSourceEtArriveDansLaDestination()
    {
        var source = new List<ShortcutEntry> { E(0, 1, 2, "Voyageuse"), E(0, 0, 0, "Reste") };
        var dest = new List<ShortcutEntry>();

        var r = ShortcutActionService.TransferCore(source, dest, [], 0, 1, 2);

        Assert.True(r.Ok);
        Assert.DoesNotContain(source, s => s.Name == "Voyageuse");
        Assert.Contains(source, s => s.Name == "Reste");
        var moved = Assert.Single(dest);
        Assert.Equal("Voyageuse", moved.Name);
        Assert.Equal((0, 0, 0), (moved.Page, moved.Row, moved.Col));
    }

    [Fact]
    public void TransferCore_ConserveLIconeDuStoreEtLesConfigsDeType()
    {
        // C'est tout l'intérêt de déplacer l'ENTREE plutôt que d'en recréer une : le store
        // d'icônes est commun aux deux grilles, donc l'icône suit sans rien retélécharger.
        var entry = new ShortcutEntry
        {
            Page = 0, Row = 0, Col = 0, Name = "Terminal", Type = ShortcutType.OpenTerminal,
            Command = @"C:\dev", IconPath = @"C:\wt.exe", IconProfilePath = @"icons\abc123.png",
            Terminal = new TerminalConfig { ExePath = "wt.exe", StartingDirectory = @"C:\dev" },
        };
        var dest = new List<ShortcutEntry>();

        var r = ShortcutActionService.TransferCore([entry], dest, [], 0, 0, 0);

        Assert.True(r.Ok);
        var moved = Assert.Single(dest);
        Assert.Equal(@"icons\abc123.png", moved.IconProfilePath);
        Assert.Equal(@"C:\wt.exe", moved.IconPath);
        Assert.Equal(ShortcutType.OpenTerminal, moved.Type);
        Assert.NotNull(moved.Terminal);
    }

    [Fact]
    public void TransferCore_CaseVide_EchoueSansRienToucher()
    {
        var source = new List<ShortcutEntry> { E(0, 0, 0) };
        var dest = new List<ShortcutEntry>();

        var r = ShortcutActionService.TransferCore(source, dest, [], 0, 3, 3);

        Assert.False(r.Ok);
        Assert.Single(source);
        Assert.Empty(dest);
    }

    [Fact]
    public void TransferCore_DestinationPleine_CreeUnePage()
    {
        var source = new List<ShortcutEntry> { E(0, 0, 0, "Voyageuse") };
        var dest = Full(0);

        var r = ShortcutActionService.TransferCore(source, dest, [], 0, 0, 0);

        Assert.True(r.Ok);
        var moved = dest.First(s => s.Name == "Voyageuse");
        Assert.Equal((1, 0, 0), (moved.Page, moved.Row, moved.Col));
    }
}
