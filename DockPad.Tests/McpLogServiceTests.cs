using DockPad.Services;

namespace DockPad.Tests;

public class McpLogServiceTests
{
    [Fact]
    public void Add_InsereEnTete_AvecIconeStatut()
    {
        McpLogService.Clear();
        McpLogService.Add("dockpad_grid_get", "", McpLogStatus.Success);
        McpLogService.Add("dockpad_shortcut_delete", "page 0", McpLogStatus.Refused, "suppression désactivée");

        Assert.Equal(2, McpLogService.Entries.Count);
        Assert.Equal("dockpad_shortcut_delete", McpLogService.Entries[0].Tool); // plus récent en tête
        Assert.Equal("🚫", McpLogService.Entries[0].StatusIcon);
        Assert.Equal("✅", McpLogService.Entries[1].StatusIcon);
    }
}
