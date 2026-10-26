using System.IO;
using System.Reflection;
using System.Text.Json;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Une mutation MCP reste enregistrée et réussie malgré un abonné défaillant ; les autres abonnés sont appelés.
/// </summary>
public class McpDispatcherMutationTests
{
    [Fact]
    public void Mutated_ExceptionDUnAbonne_MutationToujoursReussie()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "mcp-mutation-" + Guid.NewGuid().ToString("N"));
        var rootField = typeof(AppPaths).GetField("_root", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Profile root field not found.");
        var previousRoot = rootField.GetValue(null);
        int calls = 0;
        Action failing = () => throw new InvalidOperationException("Subscriber failed.");
        Action following = () => calls++;
        Directory.CreateDirectory(directory);
        rootField.SetValue(null, directory);
        McpDispatcher.Mutated += failing;
        McpDispatcher.Mutated += following;
        try
        {
            using var response = JsonDocument.Parse(McpDispatcher.Handle(
                """{"tool":"dockpad_page_add","args":{}}""", new McpConfig()));

            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("error").ValueKind);
            Assert.Equal(1, calls);
            var page = Assert.Single(PageConfigService.Load(TileStore.FilesFor(TileTarget.Shortcuts).PagesPath));
            Assert.Equal(response.RootElement.GetProperty("data").GetProperty("index").GetInt32(), page.Index);
        }
        finally
        {
            McpDispatcher.Mutated -= failing;
            McpDispatcher.Mutated -= following;
            rootField.SetValue(null, previousRoot);
            Directory.Delete(directory, recursive: true);
        }
    }
}
