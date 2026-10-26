using System.IO;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.StreamDock;

namespace DockPad.Tests;

/// <summary>
/// Accueil, pages et groupes : icônes, emplacements, exclusion des groupes imbriqués, pagination et navigation cyclique.
/// Maintien après modification : page garnie ou vidée, feuille disparue, groupe conservé ou remplacé et feuille de retour.
/// </summary>
public class DockLayoutTests
{
    [Fact]
    public void Accueil_SeulementLesPagesAvecTuiles()
    {
        ShortcutEntry[] entries = [Tile(page: 2), Tile(page: 0), Tile(page: 0, col: 1), new() { Page = 1 }];
        PageConfig[] pages = [new() { Index = 1 }, new() { Index = 3 }];

        var view = DockLayout.Build(new DockScreen.Home(0), entries, pages, keyCount: 15);

        Assert.Equal([0, 2], DockLayout.PagesWithTiles(entries));
        Assert.Equal([0, 2], view.Keys.OfType<DockKey.OpenPage>().Select(key => key.PageIndex));
        Assert.Equal(new DockScreen.Page(2, 0), DockLayout.Navigate(view, view.Keys[1]));
        Assert.Equal(15, view.Keys.Count);
        Assert.All(view.Keys.Skip(2), key => Assert.IsType<DockKey.Empty>(key));
    }

    [Fact]
    public void Accueil_15Pages_SansSuite()
    {
        var view = DockLayout.Build(new DockScreen.Home(0), PageEntries(15), [], keyCount: 15);

        Assert.Equal(Enumerable.Range(0, 15), view.Keys.OfType<DockKey.OpenPage>().Select(key => key.PageIndex));
        Assert.Equal(1, view.SheetCount);
        Assert.Empty(view.Keys.OfType<DockKey.Next>());
    }

    [Fact]
    public void Accueil_16Pages_SuiteEtCycle()
    {
        var entries = PageEntries(16);
        var first = DockLayout.Build(new DockScreen.Home(0), entries, [], keyCount: 15);

        Assert.Equal(Enumerable.Range(0, 14), first.Keys.OfType<DockKey.OpenPage>().Select(key => key.PageIndex));
        Assert.Equal(2, first.SheetCount);
        Assert.IsType<DockKey.Next>(first.Keys[14]);
        Assert.Equal(new DockScreen.Home(1), DockLayout.Navigate(first, first.Keys[14]));

        var last = DockLayout.Build(new DockScreen.Home(1), entries, [], keyCount: 15);

        Assert.Equal([14, 15], last.Keys.OfType<DockKey.OpenPage>().Select(key => key.PageIndex));
        Assert.Equal(2, last.SheetCount);
        Assert.All(last.Keys.Skip(2).Take(12), key => Assert.IsType<DockKey.Empty>(key));
        Assert.IsType<DockKey.Next>(last.Keys[14]);
        Assert.Equal(new DockScreen.Home(0), DockLayout.Navigate(last, last.Keys[14]));
    }

    [Fact]
    public void Page_AccueilEnHautAGaucheEtOrdreDeLaGrille()
    {
        var first = Tile(page: 2, row: 0, col: 4);
        var second = Tile(page: 2, row: 1, col: 0);
        var third = Tile(page: 2, row: 1, col: 5);
        ShortcutEntry[] entries = [third, new() { Page = 2 }, second, Tile(page: 0), first];

        var view = DockLayout.Build(new DockScreen.Page(2, 0), entries, [], keyCount: 15);

        Assert.IsType<DockKey.GoHome>(view.Keys[0]);
        Assert.Equal(new DockScreen.Home(0), DockLayout.Navigate(view, view.Keys[0]));
        Assert.Equal([first, second, third], view.Keys.OfType<DockKey.RunTile>().Select(key => key.Entry));
        Assert.All(view.Keys.Skip(4), key => Assert.IsType<DockKey.Empty>(key));
    }

    [Fact]
    public void Page_14Tuiles_SansSuite()
    {
        var entries = Tiles(count: 14, page: 2);
        var view = DockLayout.Build(new DockScreen.Page(2, 0), entries, [], keyCount: 15);

        Assert.IsType<DockKey.GoHome>(view.Keys[0]);
        Assert.Equal(entries, view.Keys.OfType<DockKey.RunTile>().Select(key => key.Entry));
        Assert.Equal(1, view.SheetCount);
        Assert.Empty(view.Keys.OfType<DockKey.Next>());
    }

    [Fact]
    public void Page_15Tuiles_13ParFeuille()
    {
        var entries = Tiles(count: 15, page: 2);
        var first = DockLayout.Build(new DockScreen.Page(2, 0), entries, [], keyCount: 15);

        Assert.IsType<DockKey.GoHome>(first.Keys[0]);
        Assert.Equal(entries.Take(13), first.Keys.OfType<DockKey.RunTile>().Select(key => key.Entry));
        Assert.Equal(2, first.SheetCount);
        Assert.IsType<DockKey.Next>(first.Keys[14]);
        Assert.Equal(new DockScreen.Page(2, 1), DockLayout.Navigate(first, first.Keys[14]));

        var last = DockLayout.Build(new DockScreen.Page(2, 1), entries, [], keyCount: 15);

        Assert.IsType<DockKey.GoHome>(last.Keys[0]);
        Assert.Equal(entries.Skip(13), last.Keys.OfType<DockKey.RunTile>().Select(key => key.Entry));
        Assert.Equal(2, last.SheetCount);
        Assert.All(last.Keys.Skip(3).Take(11), key => Assert.IsType<DockKey.Empty>(key));
        Assert.IsType<DockKey.Next>(last.Keys[14]);
        Assert.Equal(new DockScreen.Page(2, 0), DockLayout.Navigate(last, last.Keys[14]));
    }

    [Fact]
    public void Page_Vide_SeulementAccueil()
    {
        var view = DockLayout.Build(new DockScreen.Page(2, 0), [Tile(page: 0), new() { Page = 2 }], [], keyCount: 15);

        Assert.Equal(new DockScreen.Page(2, 0), view.Screen);
        Assert.Equal(15, view.Keys.Count);
        Assert.IsType<DockKey.GoHome>(view.Keys[0]);
        Assert.All(view.Keys.Skip(1), key => Assert.IsType<DockKey.Empty>(key));
    }

    [Fact]
    public void Groupe_RetourPuisEnfantsDansLOrdre()
    {
        var first = Tile(page: 2, row: 1, col: 5);
        first.Slot = 0;
        var last = Tile(page: 2, row: 1, col: 5);
        last.Slot = 3;
        var group = Group(page: 2, row: 1, col: 5);
        group.Children = [first, null, new() { Slot = 2 }, last];
        var screen = new DockScreen.Group(PageIndex: 2, Row: 1, Col: 5, PageSheet: 0, Sheet: 0);

        var view = DockLayout.Build(screen, [Tile(page: 2), group], [], keyCount: 15);

        Assert.Equal(screen, view.Screen);
        Assert.IsType<DockKey.Back>(view.Keys[0]);
        Assert.Same(first, Assert.IsType<DockKey.RunTile>(view.Keys[1]).Entry);
        Assert.Same(last, Assert.IsType<DockKey.RunTile>(view.Keys[2]).Entry);
        Assert.All(view.Keys.Skip(3), key => Assert.IsType<DockKey.Empty>(key));
    }

    [Fact]
    public void Groupe_RetourRevientALaMemeFeuille()
    {
        var group = Group(page: 2, row: 3, col: 5);
        var entries = Tiles(count: 14, page: 2).Append(group).ToArray();
        var page = DockLayout.Build(new DockScreen.Page(2, 1), entries, [], keyCount: 15);
        var groupKey = Assert.IsType<DockKey.OpenGroup>(page.Keys[2]);
        var screen = new DockScreen.Group(PageIndex: 2, Row: 3, Col: 5, PageSheet: 1, Sheet: 0);

        Assert.Same(group, groupKey.Entry);
        Assert.Equal(screen, DockLayout.Navigate(page, groupKey));
        var view = DockLayout.Build(screen, entries, [], keyCount: 15);
        Assert.Equal(new DockScreen.Page(2, 1), DockLayout.Navigate(view, view.Keys[0]));
    }

    [Fact]
    public void Maintien_PageToujoursGarnie_Gardee()
    {
        var current = new DockScreen.Page(2, 1);
        Assert.Equal(current, DockLayout.Keep(current, Tiles(count: 15, page: 2)));
    }

    [Fact]
    public void Maintien_PageVidee_RetourAccueil()
    {
        ShortcutEntry[] entries = [Tile(page: 0), new() { Page = 2 }];
        Assert.Equal(new DockScreen.Home(0), DockLayout.Keep(new DockScreen.Page(2, 0), entries));
        Assert.Equal(new DockScreen.Home(0), DockLayout.Keep(
            new DockScreen.Group(PageIndex: 2, Row: 0, Col: 0, PageSheet: 1, Sheet: 0), entries));
    }

    [Fact]
    public void Maintien_FeuilleDisparue_DerniereFeuille()
    {
        Assert.Equal(new DockScreen.Home(1), DockLayout.Keep(new DockScreen.Home(4), PageEntries(16)));
        Assert.Equal(new DockScreen.Home(0), DockLayout.Keep(new DockScreen.Home(1), PageEntries(15)));
        Assert.Equal(new DockScreen.Page(2, 1), DockLayout.Keep(new DockScreen.Page(2, 4), Tiles(count: 15, page: 2)));
        Assert.Equal(new DockScreen.Page(2, 0), DockLayout.Keep(new DockScreen.Page(2, 1), Tiles(count: 14, page: 2)));
    }

    [Fact]
    public void Maintien_GroupeRemplaceParTuileSimple_RetourPage()
    {
        var current = new DockScreen.Group(PageIndex: 2, Row: 3, Col: 5, PageSheet: 1, Sheet: 0);
        var entries = Tiles(count: 14, page: 2).Append(Tile(page: 2, row: 3, col: 5)).ToArray();

        Assert.Equal(new DockScreen.Page(2, 1), DockLayout.Keep(current, entries));
    }

    [Fact]
    public void Maintien_GroupeToujoursLa_Garde()
    {
        var current = new DockScreen.Group(PageIndex: 2, Row: 3, Col: 5, PageSheet: 1, Sheet: 0);
        var entries = Tiles(count: 14, page: 2).Append(Group(page: 2, row: 3, col: 5)).ToArray();

        Assert.Equal(current, DockLayout.Keep(current, entries));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("icons/page.png")]
    public void Accueil_IconeDePageOrigineAbsente_RepliSurLeProfil(string? profilePath)
    {
        var config = new PageConfig
        {
            Index = 2, IconProfilePath = profilePath,
            IconPath = Path.Combine(AppContext.BaseDirectory, $"missing-page-icon-{Guid.NewGuid():N}.ico"),
        };
        var view = DockLayout.Build(new DockScreen.Home(0), [Tile(page: 2)], [config], keyCount: 15);
        var key = Assert.IsType<DockKey.OpenPage>(view.Keys[0]);
        string? expected = string.IsNullOrWhiteSpace(profilePath)
            ? null : Path.Combine(IconStoreService.ProfileRoot, profilePath);

        Assert.Equal(expected, key.IconRef);
    }

    [Fact]
    public void Accueil_SansPages_TouchesVides()
    {
        var view = DockLayout.Build(new DockScreen.Home(7), [], [], keyCount: 15);

        Assert.Equal(new DockScreen.Home(0), view.Screen);
        Assert.Equal(15, view.Keys.Count);
        Assert.All(view.Keys, key => Assert.IsType<DockKey.Empty>(key));
    }

    [Fact]
    public void Accueil_GroupeSansNom_CompteCommeTuile()
    {
        var view = DockLayout.Build(new DockScreen.Home(0), [Group(page: 2)], [], keyCount: 15);

        Assert.Equal([2], DockLayout.PagesWithTiles([Group(page: 2)]));
        Assert.Equal(new DockKey.OpenPage(2, null), view.Keys[0]);
    }

    [Fact]
    public void Groupe_SansEnfants_SeulementRetour()
    {
        var group = Group(page: 2);
        group.Children = null;
        var view = DockLayout.Build(new DockScreen.Group(2, 0, 0, 0, 0), [group], [], keyCount: 15);

        Assert.IsType<DockKey.Back>(view.Keys[0]);
        Assert.All(view.Keys.Skip(1), key => Assert.IsType<DockKey.Empty>(key));
    }

    [Fact]
    public void Maintien_GroupeSupprime_RetourPageEtFeuilleAjustee()
    {
        var current = new DockScreen.Group(PageIndex: 2, Row: 3, Col: 5, PageSheet: 1, Sheet: 0);
        Assert.Equal(new DockScreen.Page(2, 0), DockLayout.Keep(current, [Tile(page: 2)]));
    }

    [Fact]
    public void Maintien_GroupeConserve_FeuilletsAjustes()
    {
        var current = new DockScreen.Group(PageIndex: 2, Row: 0, Col: 0, PageSheet: 4, Sheet: 3);
        Assert.Equal(new DockScreen.Group(2, 0, 0, 0, 0), DockLayout.Keep(current, [Group(page: 2)]));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(8, 1)]
    public void Disposition_FeuilleHorsLimites_RameneeALaDerniere(int sheet, int expectedSheet)
    {
        Assert.Equal(new DockScreen.Home(expectedSheet), DockLayout.Build(
            new DockScreen.Home(sheet), PageEntries(16), [], keyCount: 15).Screen);
        Assert.Equal(new DockScreen.Page(2, expectedSheet), DockLayout.Build(
            new DockScreen.Page(2, sheet), Tiles(count: 15, page: 2), [], keyCount: 15).Screen);
        Assert.Equal(new DockScreen.Group(2, 0, 0, 0, 0), DockLayout.Build(
            new DockScreen.Group(2, 0, 0, 0, sheet), [Group(page: 2)], [], keyCount: 15).Screen);
    }

    [Fact]
    public void Navigation_TuileSimpleOuCaseVide_SansChangementDEcran()
    {
        var view = DockLayout.Build(new DockScreen.Page(0, 0), [Tile(page: 0)], [], keyCount: 15);

        Assert.Null(DockLayout.Navigate(view, view.Keys[1]));
        Assert.Null(DockLayout.Navigate(view, view.Keys[2]));
    }

    [Fact]
    public void Disposition_NombreDeTouchesAdapte_PaginationCoherente()
    {
        var group = Group(page: 2, layout: TileLayout.TwoPlusFour);
        group.Children = Tiles(count: 6, page: 2).Cast<ShortcutEntry?>().ToList();
        var first = DockLayout.Build(new DockScreen.Group(2, 0, 0, 0, 0), [group], [], keyCount: 6);
        var last = DockLayout.Build(new DockScreen.Group(2, 0, 0, 0, 1), [group], [], keyCount: 6);

        Assert.Equal(6, first.Keys.Count);
        Assert.Equal(2, first.SheetCount);
        Assert.Equal(4, first.Keys.OfType<DockKey.RunTile>().Count());
        Assert.Equal(new DockScreen.Group(2, 0, 0, 0, 1), DockLayout.Navigate(first, first.Keys[5]));
        Assert.Equal(2, last.Keys.OfType<DockKey.RunTile>().Count());
        Assert.Equal(2, last.SheetCount);
        Assert.Equal(new DockScreen.Group(2, 0, 0, 0, 0), DockLayout.Navigate(last, last.Keys[5]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Disposition_TropPeuDeTouches_Refusee(int keyCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DockLayout.Build(new DockScreen.Home(0), [], [], keyCount));
    }

    [Fact]
    public void Groupe_EmplacementsRenseignes_TriesParSlot()
    {
        var first = Tile(page: 2);
        first.Slot = 0;
        var last = Tile(page: 2);
        last.Slot = 3;
        var group = Group(page: 2);
        group.Children = [last, null, first];

        var view = DockLayout.Build(new DockScreen.Group(2, 0, 0, 0, 0), [group], [], keyCount: 15);

        Assert.Equal([first, last], view.Keys.OfType<DockKey.RunTile>().Select(key => key.Entry));
        Assert.Equal([last, null, first], group.Children);
        Assert.Equal(0, first.Slot);
        Assert.Equal(3, last.Slot);
    }

    [Fact]
    public void Groupe_EmplacementsSansSlot_OrdreDeLaListeConserve()
    {
        var first = Tile(page: 2, col: 5);
        var last = Tile(page: 2, col: 0);
        var group = Group(page: 2);
        group.Children = [first, null, last];

        var view = DockLayout.Build(new DockScreen.Group(2, 0, 0, 0, 0), [group], [], keyCount: 15);

        Assert.Equal([first, last], view.Keys.OfType<DockKey.RunTile>().Select(key => key.Entry));
        Assert.Null(first.Slot);
        Assert.Null(last.Slot);
    }

    [Fact]
    public void Accueil_IconeDePageOrigineExistante_PrioritaireSurLeProfil()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, $"page-icon-{Guid.NewGuid():N}.ico");
        File.WriteAllBytes(iconPath, []);
        try
        {
            var config = new PageConfig { Index = 2, IconPath = iconPath, IconProfilePath = "icons/page.png" };

            var view = DockLayout.Build(new DockScreen.Home(0), [Tile(page: 2)], [config], keyCount: 15);

            Assert.Equal(iconPath, Assert.IsType<DockKey.OpenPage>(view.Keys[0]).IconRef);
        }
        finally
        {
            File.Delete(iconPath);
        }
    }

    [Fact]
    public void Accueil_IconeDePageOrigineVide_RepliSurLeProfil()
    {
        var config = new PageConfig { Index = 2, IconProfilePath = "icons/page.png" };

        var view = DockLayout.Build(new DockScreen.Home(0), [Tile(page: 2)], [config], keyCount: 15);

        Assert.Equal(Path.Combine(IconStoreService.ProfileRoot, "icons/page.png"),
            Assert.IsType<DockKey.OpenPage>(view.Keys[0]).IconRef);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    public void Navigation_VueConstruiteExplicitement_UtiliseLeNombreDeFeuilles(int sheet, int expectedSheet)
    {
        var next = new DockKey.Next();
        DockKey[] keys = Enumerable.Repeat<DockKey>(new DockKey.Empty(), 15).ToArray();
        keys[^1] = next;
        var view = new DockView(new DockScreen.Home(sheet), keys, SheetCount: 3);

        Assert.Equal(new DockScreen.Home(expectedSheet), DockLayout.Navigate(view, next));
    }

    [Fact]
    public void Maintien_GroupeToujoursValide_ConserveLaFeuilleDePageAuRetour()
    {
        var current = new DockScreen.Group(PageIndex: 2, Row: 3, Col: 5, PageSheet: 1, Sheet: 0);
        var entries = Tiles(count: 14, page: 2).Append(Group(page: 2, row: 3, col: 5)).ToArray();

        var kept = DockLayout.Keep(current, entries);
        var view = DockLayout.Build(kept, entries, [], keyCount: 15);

        Assert.Equal(current, view.Screen);
        Assert.Equal(new DockScreen.Page(2, 1), DockLayout.Navigate(view, view.Keys[0]));
    }

    [Fact]
    public void Groupe_EmplacementsMixtes_SlotOuIndiceDansLaListe()
    {
        var first = Tile(page: 2);
        first.Slot = 0;
        var middle = Tile(page: 2, col: 1);
        var last = Tile(page: 2, col: 2);
        last.Slot = 3;
        var group = Group(page: 2);
        group.Children = [last, null, middle, first];

        var view = DockLayout.Build(new DockScreen.Group(2, 0, 0, 0, 0), [group], [], keyCount: 15);

        Assert.Equal([first, middle, last], view.Keys.OfType<DockKey.RunTile>().Select(key => key.Entry));
        Assert.Equal([last, null, middle, first], group.Children);
        Assert.Null(middle.Slot);
    }

    [Fact]
    public void Groupe_EnfantLuiMemeGroupe_ExcluDuSousEcran()
    {
        var child = Tile(page: 2);
        var group = Group(page: 2);
        group.Children = [Group(page: 2), null, child];

        var view = DockLayout.Build(new DockScreen.Group(2, 0, 0, 0, 0), [group], [], keyCount: 15);

        Assert.IsType<DockKey.Back>(view.Keys[0]);
        Assert.Same(child, Assert.IsType<DockKey.RunTile>(view.Keys[1]).Entry);
        Assert.All(view.Keys.Skip(2), key => Assert.IsType<DockKey.Empty>(key));
        Assert.Equal(1, view.SheetCount);
    }

    private static ShortcutEntry Tile(int page, int row = 0, int col = 0) => new()
    {
        Page = page, Row = row, Col = col, Name = $"Tile {page}-{row}-{col}", Command = "sample.exe",
    };

    private static ShortcutEntry[] Tiles(int count, int page) => Enumerable.Range(0, count)
        .Select(index => Tile(page, row: index / 6, col: index % 6)).ToArray();

    private static ShortcutEntry[] PageEntries(int count) => Enumerable.Range(0, count)
        .Select(page => Tile(page)).Reverse().ToArray();

    private static ShortcutEntry Group(int page, int row = 0, int col = 0, TileLayout layout = TileLayout.Quad) => new()
    {
        Page = page, Row = row, Col = col, Layout = layout, Children = [Tile(page)],
    };
}
