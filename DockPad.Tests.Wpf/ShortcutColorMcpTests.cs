using System.Text.Json;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class ShortcutColorMcpTests
{
    private static JsonElement Call(string tool, object args)
    {
        using var response = JsonDocument.Parse(McpDispatcher.Handle(
            JsonSerializer.Serialize(new { tool, args }), new McpConfig { Enabled = true }));
        return response.RootElement.Clone();
    }

    [Theory]
    [InlineData("shortcuts", @"C:\dev\DockPad")]
    [InlineData("favorites", @"C:\dev\DockPad")]
    [InlineData("shortcuts", "\"C:\\dev\\DockPad\"")]
    [InlineData("favorites", "  \"C:\\dev\\DockPad\"  ")]
    public Task JsonMcp_CreationModificationEtLecture_DeLaCouleur(string target, string folder) => WpfTestHost.Run(() =>
    {
        var files = TileStore.FilesFor(TileStore.Parse(target));
        var before = ShortcutService.Load(files.EntriesPath);
        try
        {
            ShortcutService.Save([], files.EntriesPath);
            var add = Call("dockpad_shortcut_add", new
            {
                target, items = new[] { new { name = "Dossier", type = "OpenFolder", command = folder, row = 0, col = 0 } },
            });
            Assert.True(add.GetProperty("ok").GetBoolean(), add.ToString());
            var grid = Call("dockpad_grid_get", new { target });
            Assert.Equal(TerminalDetectionService.TabColor("DockPad"),
                grid.GetProperty("data").GetProperty("shortcuts")[0].GetProperty("color").GetString());

            var update = Call("dockpad_shortcut_update", new
            {
                target, page = 0, row = 0, col = 0, changes = new { color = "#abcdef" },
            });
            Assert.True(update.GetProperty("ok").GetBoolean(), update.ToString());
            Assert.Equal("#ABCDEF", Assert.Single(ShortcutService.Load(files.EntriesPath)).Color);

            var invalid = Call("dockpad_shortcut_update", new
            {
                target, page = 0, row = 0, col = 0, changes = new { color = "#123", name = "Refusé" },
            });
            Assert.False(invalid.GetProperty("ok").GetBoolean());
            Assert.Contains("#RRGGBB", invalid.GetProperty("error").GetString());
            var entry = Assert.Single(ShortcutService.Load(files.EntriesPath));
            Assert.Equal("#ABCDEF", entry.Color);
            Assert.Equal("Dossier", entry.Name);

            var clear = Call("dockpad_shortcut_update", new
            {
                target, page = 0, row = 0, col = 0, changes = new { color = "" },
            });
            Assert.True(clear.GetProperty("ok").GetBoolean(), clear.ToString());
            Assert.Null(Assert.Single(ShortcutService.Load(files.EntriesPath)).Color);

            var invalidAdd = Call("dockpad_shortcut_add", new
            {
                target, items = new[] { new { name = "Refusé", type = "OpenFolder", command = @"C:\dev\Autre", color = "rouge" } },
            });
            Assert.False(invalidAdd.GetProperty("ok").GetBoolean());
            Assert.Contains("#RRGGBB", invalidAdd.GetProperty("error").GetString());
            Assert.Single(ShortcutService.Load(files.EntriesPath));
        }
        finally { ShortcutService.Save(before, files.EntriesPath); }
    });
}
