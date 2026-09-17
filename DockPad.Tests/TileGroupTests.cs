using System.IO;
using System.Text.Json;
using System.Windows;
using DockPad.Models;
using DockPad.Services;
using DockPad.Views;

namespace DockPad.Tests;

public class TileGroupTests
{
    private static ShortcutEntry Item(string name, int col = 0) => new()
    {
        Name = name, Col = col, Command = "cmd.exe", IconProfilePath = "icons/example.png",
        Terminal = new TerminalConfig { ExePath = "wt.exe", ExtraArgs = "--test" },
    };

    private static ShortcutEntry Group(TileLayout layout, params ShortcutEntry?[] children)
    {
        var group = new ShortcutEntry { Layout = layout, Children = children.ToList() };
        TileGroupService.Normalize([group]);
        return group;
    }

    [Theory]
    [InlineData(TileLayout.Quad, 4)]
    [InlineData(TileLayout.TwoPlusFour, 6)]
    public void ConvertSingle_PreservesEntireShortcut(TileLayout layout, int capacity)
    {
        var shortcut = Item("Terminal");
        var all = new List<ShortcutEntry> { shortcut };
        Assert.True(TileGroupService.ChangeLayoutCore(all, new(0, 0, 0), layout).Ok);
        var group = Assert.Single(all);
        Assert.Equal(capacity, group.Children!.Count);
        Assert.Same(shortcut, group.Children[0]);
        Assert.Equal("--test", group.Children[0]!.Terminal!.ExtraArgs);
        Assert.Equal("icons/example.png", group.Children[0]!.IconProfilePath);
        Assert.All(group.Children.Skip(1), child => Assert.Null(child));
    }

    [Theory]
    [InlineData(TileLayout.Quad, 5)]
    [InlineData(TileLayout.Simple, 2)]
    public void ReduceWithTooManyChildren_RefusesWithoutMutation(TileLayout target, int count)
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.TwoPlusFour,
            Enumerable.Range(0, count).Select(i => Item(i.ToString())).ToArray()) };
        var before = JsonSerializer.Serialize(all);
        var result = TileGroupService.ChangeLayoutCore(all, new(0, 0, 0), target);
        Assert.False(result.Ok);
        Assert.Equal(before, JsonSerializer.Serialize(all));
    }

    [Fact]
    public void ReduceAfterManualExtraction_CompactsInReadingOrder()
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.TwoPlusFour, Item("A"), null, Item("B"), null, Item("C"), Item("D")) };
        all[0].Name = "Développement";
        Assert.True(TileGroupService.ChangeLayoutCore(all, new(0, 0, 0), TileLayout.Quad).Ok);
        Assert.Equal(new[] { "A", "B", "C", "D" }, all[0].Children!.Select(c => c!.Name));
        Assert.Equal("Développement", all[0].Name);
    }

    [Fact]
    public void ConvertLastChildToSingle_RemovesContainer()
    {
        var child = Item("A");
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, null, null, child) };
        Assert.True(TileGroupService.ChangeLayoutCore(all, new(0, 0, 0), TileLayout.Simple).Ok);
        Assert.Same(child, Assert.Single(all));
        Assert.Null(child.Slot);
        Assert.False(child.IsGroup);
    }

    [Fact]
    public void RenameGroup_PreservesChildNamesAndRejectsEmptyName()
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, Item("Terminal")) };
        Assert.True(TileGroupService.RenameCore(all, new(0, 0, 0), "  Outils  ").Ok);
        Assert.Equal("Outils", all[0].Name);
        Assert.Equal("Terminal", all[0].Children![0]!.Name);
        Assert.False(TileGroupService.RenameCore(all, new(0, 0, 0), "  ").Ok);
        Assert.Equal("Outils", all[0].Name);
    }

    [Fact]
    public void EmptyGroup_CanBeCreatedAndReturnedToEmptySlot()
    {
        var all = new List<ShortcutEntry>();
        Assert.True(TileGroupService.ChangeLayoutCore(all, new(0, 1, 2), TileLayout.Quad).Ok);
        Assert.True(Assert.Single(all).IsGroup);
        Assert.True(TileGroupService.ChangeLayoutCore(all, new(0, 1, 2), TileLayout.Simple).Ok);
        Assert.Empty(all);
    }

    [Fact]
    public void EditGroup_ColorSurvivesLayoutCloneAndPersistence()
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, Item("Terminal")) };
        Assert.True(TileGroupService.UpdateCore(all, new(0, 0, 0), "Outils", "#12ab34").Ok);
        Assert.True(TileGroupService.ChangeLayoutCore(all, new(0, 0, 0), TileLayout.TwoPlusFour).Ok);
        var copy = TileGroupService.Clone(all[0]);
        Assert.Equal("#12AB34", copy.GroupColor);
        Assert.Equal("Outils", copy.Name);
        Assert.Equal("Terminal", copy.Children![0]!.Name);
        Assert.Null(copy.Children[0]!.GroupColor);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345Z")]
    [InlineData("#12345678")]
    public void EditGroup_InvalidColorDoesNotChangeNameOrColor(string color)
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, Item("A")) };
        all[0].Name = "Outils";
        var before = JsonSerializer.Serialize(all);
        Assert.False(TileGroupService.UpdateCore(all, new(0, 0, 0), "Changed", color).Ok);
        Assert.Equal(before, JsonSerializer.Serialize(all));
        Assert.Equal(TileGroupService.DefaultColor, TileGroupService.EffectiveColor(color));
    }

    [Fact]
    public void MoveGridToGroup_ThenOut_PreservesIdentityAndEmptySlots()
    {
        var shortcut = Item("A", 1);
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad), shortcut };
        Assert.True(TileGroupService.MoveCore(all, new(0, 0, 1), new(0, 0, 0, 3)).Ok);
        Assert.Single(all);
        Assert.Same(shortcut, all[0].Children![3]);
        Assert.Equal(3, shortcut.Slot);
        Assert.True(TileGroupService.MoveCore(all, new(0, 0, 0, 3), new(0, 2, 4)).Ok);
        Assert.Null(all[0].Children![3]);
        Assert.Same(shortcut, TileGroupService.Get(all, new(0, 2, 4)));
        Assert.Null(shortcut.Slot);
    }

    [Fact]
    public void ExchangeChildWithGridShortcut_PreservesBoth()
    {
        var a = Item("A"); var b = Item("B", 1);
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, a), b };
        Assert.True(TileGroupService.MoveCore(all, new(0, 0, 0, 0), new(0, 0, 1)).Ok);
        Assert.Same(b, TileGroupService.Get(all, new(0, 0, 0, 0)));
        Assert.Same(a, TileGroupService.Get(all, new(0, 0, 1)));
        Assert.Equal(0, b.Slot);
        Assert.Null(a.Slot);
    }

    [Fact]
    public void ExchangeTwoRootShortcuts_DoesNotLoseOneThroughCoordinateMutation()
    {
        var a = Item("A"); var b = Item("B", 1);
        var all = new List<ShortcutEntry> { a, b };
        Assert.True(TileGroupService.MoveCore(all, new(0, 0, 0), new(0, 0, 1)).Ok);
        Assert.Equal(2, all.Count);
        Assert.Same(b, TileGroupService.Get(all, new(0, 0, 0)));
        Assert.Same(a, TileGroupService.Get(all, new(0, 0, 1)));
    }

    [Fact]
    public void ExchangeWithinGroup_DoesNotCompactOtherSlots()
    {
        var a = Item("A"); var b = Item("B");
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, a, null, b) };
        Assert.True(TileGroupService.MoveCore(all, new(0, 0, 0, 0), new(0, 0, 0, 2)).Ok);
        Assert.Equal(new string?[] { "B", null, "A", null }, all[0].Children!.Select(c => c?.Name));
    }

    [Fact]
    public void MoveBetweenGroups_PreservesDestinationLayout()
    {
        var a = Group(TileLayout.Quad, Item("A"));
        var b = Group(TileLayout.TwoPlusFour); b.Col = 1;
        var all = new List<ShortcutEntry> { a, b };
        Assert.True(TileGroupService.MoveCore(all, new(0, 0, 0, 0), new(0, 0, 1, 5)).Ok);
        Assert.Null(a.Children![0]);
        Assert.Equal("A", b.Children![5]!.Name);
        Assert.Equal(TileLayout.TwoPlusFour, b.Layout);
    }

    [Fact]
    public void MoveWholeGroup_UpdatesAllChildAddresses()
    {
        var group = Group(TileLayout.Quad, Item("A"), null, Item("B"));
        var all = new List<ShortcutEntry> { group, Item("C", 1) };
        Assert.True(TileGroupService.MoveCore(all, new(0, 0, 0), new(0, 0, 1)).Ok);
        Assert.Same(group, TileGroupService.Get(all, new(0, 0, 1)));
        Assert.All(group.Children!.OfType<ShortcutEntry>(), child => Assert.Equal(1, child.Col));
        Assert.Equal("C", TileGroupService.Get(all, new(0, 0, 0))!.Name);
    }

    [Fact]
    public void CannotNestGroupOrReplaceParentWithItsChild()
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, Item("A")) };
        var before = JsonSerializer.Serialize(all);
        Assert.False(TileGroupService.MoveCore(all, new(0, 0, 0), new(0, 0, 0, 1)).Ok);
        Assert.False(TileGroupService.MoveCore(all, new(0, 0, 0, 0), new(0, 0, 0)).Ok);
        Assert.False(TileGroupService.MoveCore(all, new(0, 0, 0, 0), new(0, 0, 0, 6)).Ok);
        Assert.False(TileGroupService.MoveCore(all, new(0, 0, 0, 0), new(0, 4, 0)).Ok);
        Assert.Equal(before, JsonSerializer.Serialize(all));
    }

    [Fact]
    public void ChildUpdateAndDelete_LeaveSiblingsAndContainerIntact()
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, Item("A"), Item("B")) };
        Assert.True(ShortcutActionService.UpdateCore(all, 0, 0, 0, new() { Name = "Updated" }, slot: 1).Ok);
        Assert.Equal("Updated", all[0].Children![1]!.Name);
        Assert.Equal("A", all[0].Children![0]!.Name);
        Assert.False(ShortcutActionService.UpdateCore(all, 0, 0, 0, new() { Command = "evil.exe" }).Ok);
        Assert.True(ShortcutActionService.DeleteCore(all, 0, 0, 0, slot: 1).Ok);
        Assert.Null(all[0].Children![1]);
        Assert.Equal(4, all[0].Children!.Count);
    }

    [Fact]
    public void TransferChildToFavorites_LeavesOtherChildrenAtSource()
    {
        var source = new List<ShortcutEntry> { Group(TileLayout.Quad, Item("A"), Item("B")) };
        var dest = new List<ShortcutEntry>();
        Assert.True(ShortcutActionService.TransferCore(source, dest, [], 0, 0, 0, childSlot: 1).Ok);
        Assert.Equal("B", Assert.Single(dest).Name);
        Assert.Null(dest[0].Slot);
        Assert.Null(source[0].Children![1]);
        Assert.Equal("A", source[0].Children![0]!.Name);
    }

    [Fact]
    public void MoveChildToPage_KeepsParentAndOtherChildren()
    {
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, Item("A"), Item("B")) };
        Assert.True(ShortcutActionService.MoveCore(all, [new() { Index = 1 }], 0, 0, 0, 1, null, null, slot: 0).Ok);
        Assert.Null(all[0].Children![0]);
        Assert.Equal("A", all.Single(e => e.Page == 1).Name);
    }

    [Fact]
    public void DuplicateGroup_DeepCopiesChildrenAndTheirSettings()
    {
        var group = Group(TileLayout.Quad, Item("A"));
        var all = new List<ShortcutEntry> { group };
        Assert.True(ShortcutActionService.DuplicateCore(all, 0, 0, 0).Ok);
        var copy = all.Single(e => e != group);
        Assert.NotSame(group.Children, copy.Children);
        Assert.NotSame(group.Children![0], copy.Children![0]);
        copy.Children[0]!.Terminal!.ExtraArgs = "changed";
        Assert.Equal("--test", group.Children[0]!.Terminal!.ExtraArgs);
        Assert.Equal(copy.Col, copy.Children[0]!.Col);
    }

    [Fact]
    public void SearchAndFavoriteLookup_IncludeChildren()
    {
        var child = Item("Documentation"); child.Type = ShortcutType.OpenUrl; child.Command = "https://example.com";
        var all = new List<ShortcutEntry> { Group(TileLayout.Quad, null, child) };
        Assert.Same(child, Assert.Single(ShortcutSearch.Filter(all, "doc")));
        Assert.Same(child, FavoriteToggle.Find(all, child.Command));
    }

    [Fact]
    public void Persistence_PreservesHolesAndLoadsLegacyEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dockpad-groups-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """[{"page":0,"row":1,"col":2,"name":"Old","command":"cmd.exe"}]""");
            var legacy = ShortcutService.Load(path);
            Assert.False(Assert.Single(legacy).IsGroup);
            ShortcutService.Save(legacy, path);
            Assert.DoesNotContain("layout", File.ReadAllText(path));
            Assert.DoesNotContain("children", File.ReadAllText(path));
            var group = Group(TileLayout.TwoPlusFour, null, Item("A"), null, null, null, Item("B"));
            group.Page = 2; group.Row = 3; group.Col = 4;
            ShortcutService.Save([group], path);
            var loaded = Assert.Single(ShortcutService.Load(path));
            Assert.Equal(new string?[] { null, "A", null, null, null, "B" }, loaded.Children!.Select(c => c?.Name));
            Assert.Equal(new TileAddress(2, 3, 4, 5), TileAddress.Of(loaded.Children![5]!));
            Assert.Equal("icons/example.png", loaded.Children![5]!.IconProfilePath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TwoPlusFour_UsesTwoThirdsForTopAndOneThirdForBottom()
    {
        var size = new Size(108, 90);
        Assert.Equal(new Rect(54, 0, 54, 60), TileGroupPanel.Bounds(TileLayout.TwoPlusFour, 1, size));
        Assert.Equal(new Rect(81, 60, 27, 30), TileGroupPanel.Bounds(TileLayout.TwoPlusFour, 5, size));
        Assert.Equal(108 * 90, Enumerable.Range(0, 6).Sum(i =>
        {
            var rect = TileGroupPanel.Bounds(TileLayout.TwoPlusFour, i, size);
            return rect.Width * rect.Height;
        }));
    }

    [Fact]
    public async Task AddInsideGroup_PersistsChildAndRefusesOccupiedSlot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dockpad-group-add-{Guid.NewGuid():N}");
        var files = new TileFiles(Path.Combine(directory, "shortcuts.json"), Path.Combine(directory, "pages.json"));
        try
        {
            ShortcutService.Save([Group(TileLayout.Quad)], files.EntriesPath);
            var item = new ShortcutAddItem { Name = "New", Command = "fixture-missing-command", Page = 0, Row = 0, Col = 0 };
            Assert.True((await ShortcutActionService.AddInGroupAsync(item, 2, files)).Ok);
            var saved = Assert.Single(ShortcutService.Load(files.EntriesPath));
            Assert.Equal("New", saved.Children![2]!.Name);
            Assert.Null(saved.Children[0]);
            item.Name = "Replacement";
            Assert.False((await ShortcutActionService.AddInGroupAsync(item, 2, files)).Ok);
            Assert.Equal("New", ShortcutService.Load(files.EntriesPath)[0].Children![2]!.Name);
        }
        finally
        {
            File.Delete(files.EntriesPath);
            File.Delete(files.PagesPath);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }
}
