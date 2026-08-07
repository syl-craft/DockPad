using System.Text.Json;
using DockPad.Models;
using DockPad.Services;
using Xunit;

// McpDispatcher.Handle écrit dans McpLogService.Entries (collection statique partagée avec
// McpLogServiceTests) : la parallélisation par défaut de xUnit entre classes de test provoque
// des faux échecs par pollution croisée. Un seul test assembly ici, donc pas d'impact perf.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DockPad.Tests;

public class McpDispatcherTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Handle_JsonInvalide_RenvoieErreur()
    {
        var resp = Parse(McpDispatcher.Handle("{pas du json", new McpConfig()));
        Assert.False(resp.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Handle_OutilInconnu_RenvoieErreur()
    {
        var resp = Parse(McpDispatcher.Handle("""{"tool":"dockpad_nope","args":{}}""", new McpConfig()));
        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Contains("inconnu", resp.GetProperty("error").GetString());
    }

    [Fact]
    public void Handle_McpDesactive_RefuseToutOutil()
    {
        var cfg = new McpConfig { Enabled = false };
        var resp = Parse(McpDispatcher.Handle("""{"tool":"dockpad_grid_get","args":{}}""", cfg));
        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Contains("désactivé", resp.GetProperty("error").GetString());
    }

    [Fact]
    public void Handle_SuppressionNonAutorisee_RefuseSansExecuter()
    {
        var cfg = new McpConfig { Enabled = true, AllowDelete = false };
        var resp = Parse(McpDispatcher.Handle(
            """{"tool":"dockpad_shortcut_delete","args":{"page":0,"row":0,"col":0}}""", cfg));
        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Contains("suppression", resp.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
