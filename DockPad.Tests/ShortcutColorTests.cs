using System.IO;
using System.Text.Json;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class ShortcutColorTests
{
    [Theory]
    [InlineData(@"C:\dev\DockPad", @"C:\dev\DockPad")]
    [InlineData(@"D:\ailleurs\DockPad\", @"D:\ailleurs\DockPad\")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"\\serveur\partage\Été", @"\\serveur\partage\Été")]
    [InlineData("\"C:\\dev\\DockPad\"", @"C:\dev\DockPad")]
    [InlineData("  \"C:\\dev\\DockPad\"  ", @"C:\dev\DockPad")]
    [InlineData("  \"C:\\dev\\Nom avec espaces\"  ", @"C:\dev\Nom avec espaces")]
    [InlineData("  C:\\dev\\DockPad  ", @"C:\dev\DockPad")]
    public void CreationDossier_MemeCouleurQueLOngletTerminal(string folder, string terminalFolder)
    {
        var entries = new List<ShortcutEntry>();
        var result = ShortcutActionService.AddCore(entries, [],
            [new() { Name = "Nom personnalisé", Type = ShortcutType.OpenFolder, Command = folder }]);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(TerminalDetectionService.TabColor(TerminalDetectionService.FolderName(terminalFolder)),
            Assert.Single(entries).Color);
        Assert.Equal(TerminalDetectionService.Expand("{color}", terminalFolder, command: ""), entries[0].Color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#123456")]
    public void DuplicateCore_ConserveLaCouleur(string? color)
    {
        var original = new ShortcutEntry { Name = "Dossier", Type = ShortcutType.OpenFolder, Command = @"C:\dev\DockPad", Color = color };
        var entries = new List<ShortcutEntry> { original };

        Assert.True(ShortcutActionService.DuplicateCore(entries, 0, 0, 0).Ok);

        var copy = Assert.Single(entries.Where(entry => !ReferenceEquals(entry, original)));
        Assert.Equal(color, copy.Color);
        Assert.Equal(original.Command, copy.Command);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#123456")]
    public void TransferCore_ConserveLaCouleur(string? color)
    {
        var original = new ShortcutEntry { Name = "Dossier", Type = ShortcutType.OpenFolder, Command = @"C:\dev\DockPad", Color = color };
        var source = new List<ShortcutEntry> { original };
        var destination = new List<ShortcutEntry>();

        Assert.True(ShortcutActionService.TransferCore(source, destination, [], 0, 0, 0).Ok);

        Assert.Empty(source);
        var moved = Assert.Single(destination);
        Assert.Equal(color, moved.Color);
        Assert.Equal(original.Command, moved.Command);
    }

    [Theory]
    [InlineData("#aBc123", "#ABC123")]
    [InlineData("", null)]
    public void Creation_CouleurExplicitePrioritaire(string color, string? expected)
    {
        var entries = new List<ShortcutEntry>();
        Assert.True(ShortcutActionService.AddCore(entries, [], [new()
        {
            Name = "Dossier", Type = ShortcutType.OpenFolder, Command = @"C:\dev\DockPad", Color = color,
        }]).Ok);
        Assert.Equal(expected, Assert.Single(entries).Color);
    }

    [Theory]
    [InlineData(ShortcutType.RunCommand)]
    [InlineData(ShortcutType.OpenUrl)]
    [InlineData(ShortcutType.OpenTerminal)]
    [InlineData(ShortcutType.SwitchToProcess)]
    public void Creation_AutresTypesSansCouleur_Inchanges(ShortcutType type)
    {
        var entries = new List<ShortcutEntry>();
        Assert.True(ShortcutActionService.AddCore(entries, [],
            [new() { Name = "Autre", Type = type, Command = "commande" }]).Ok);
        Assert.Null(Assert.Single(entries).Color);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#123")]
    [InlineData("#12345678")]
    [InlineData("#GG1234")]
    [InlineData("#123456\n")]
    [InlineData(" ")]
    public void CouleurInvalide_RefuseLeLotEtLaModificationSansMutation(string color)
    {
        var entry = new ShortcutEntry { Name = "Avant", Command = "cmd", Color = "#123456" };
        var entries = new List<ShortcutEntry> { entry };
        var add = ShortcutActionService.AddCore(entries, [],
            [new() { Name = "Valide", Command = "cmd" }, new() { Name = "Invalide", Command = "cmd", Color = color }]);
        Assert.False(add.Ok);
        Assert.Contains("#RRGGBB", add.Error);
        Assert.Single(entries);

        var update = ShortcutActionService.UpdateCore(entries, 0, 0, 0,
            new() { Name = "Après", Color = color });
        Assert.False(update.Ok);
        Assert.Contains("#RRGGBB", update.Error);
        Assert.Equal("Avant", entry.Name);
        Assert.Equal("#123456", entry.Color);
    }

    [Fact]
    public void Update_CouleurOmiseConserve_VideEfface_SansRecalculAutomatique()
    {
        var entry = new ShortcutEntry { Name = "Dossier", Type = ShortcutType.OpenFolder, Command = @"C:\a", Color = "#123456" };
        var entries = new List<ShortcutEntry> { entry };
        Assert.True(ShortcutActionService.UpdateCore(entries, 0, 0, 0, new() { Command = @"C:\b" }).Ok);
        Assert.Equal("#123456", entry.Color);
        Assert.True(ShortcutActionService.UpdateCore(entries, 0, 0, 0, new() { Color = "#abcdef" }).Ok);
        Assert.Equal("#ABCDEF", entry.Color);
        Assert.True(ShortcutActionService.UpdateCore(entries, 0, 0, 0, new() { Color = "" }).Ok);
        Assert.Null(entry.Color);
        Assert.True(ShortcutActionService.UpdateCore(entries, 0, 0, 0, new() { Command = @"C:\c" }).Ok);
        Assert.Null(entry.Color);
    }

    [Theory]
    [InlineData("shortcuts.json")]
    [InlineData("favorites.json")]
    public void AncienJson_ReluEtReecritSansMigrationNiChampVide(string filename)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dockpad-color-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        try
        {
            File.WriteAllText(path, """[{"page":2,"row":1,"col":3,"name":"Dossier","type":"OpenFolder","command":"C:\\dev\\DockPad","iconPath":"","iconProfilePath":"icons/folder.png"}]""");
            var entries = ShortcutService.Load(path);
            var entry = Assert.Single(entries);
            Assert.Null(entry.Color);
            Assert.Equal((2, 1, 3, "Dossier", @"C:\dev\DockPad", "icons/folder.png"),
                (entry.Page, entry.Row, entry.Col, entry.Name, entry.Command, entry.IconProfilePath));
            entry.Color = "";
            ShortcutService.Save(entries, path);
            Assert.DoesNotContain("\"color\"", File.ReadAllText(path));
            entry.Color = "#123456";
            ShortcutService.Save(entries, path);
            Assert.Equal(entry.Color, Assert.Single(ShortcutService.Load(path)).Color);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Groupe_CreationEtUpdateEnfant_GrilleExposeLesCouleurs()
    {
        var entries = new List<ShortcutEntry>();
        Assert.True(ShortcutActionService.GroupSetCore(entries, [], 0, 0, 0, TileLayout.Quad, "G", "#654321").Ok);
        Assert.True(ShortcutActionService.AddCore(entries, [], [new()
        {
            Name = "Dossier", Type = ShortcutType.OpenFolder, Command = @"C:\dev\DockPad", Row = 0, Col = 0, Slot = 1,
        }]).Ok);
        var child = entries[0].Children?[1] ?? throw new InvalidOperationException();
        Assert.Equal(TerminalDetectionService.TabColor("DockPad"), child.Color);
        Assert.True(ShortcutActionService.UpdateCore(entries, 0, 0, 0, new() { Color = "#abcdef" }, slot: 1).Ok);
        var grid = JsonSerializer.SerializeToElement(ShortcutActionService.GetGridCore(entries, [], null).Data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var group = grid.GetProperty("shortcuts")[0];
        Assert.Equal("#654321", group.GetProperty("groupColor").GetString());
        Assert.Equal("#ABCDEF", group.GetProperty("children")[1].GetProperty("color").GetString());
        var simple = JsonSerializer.SerializeToElement(ShortcutActionService.GetGridCore([child], [], null).Data);
        Assert.Equal("#ABCDEF", simple.GetProperty("shortcuts")[0].GetProperty("color").GetString());
    }
}
