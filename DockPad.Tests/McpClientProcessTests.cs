using DockPad.Mcp;

namespace DockPad.Tests;

public class McpClientProcessTests
{
    private static readonly DateTime Started = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LanceursIntercales_SurveilleLePremierAncetreClient()
    {
        McpProcessEntry[] entries = [new(4, 3, "DockPad.exe", Started.AddSeconds(3)),
            new(3, 2, "DOCKPAD.EXE", Started.AddSeconds(2)), new(2, 1, "DockPad.exe", Started.AddSeconds(1)),
            new(1, 0, "claude.exe", Started)];

        var selected = McpClientProcess.SelectClient(4, entries);

        Assert.Equal(McpClientState.Running, selected.State);
        Assert.Equal(entries[3], selected.Client);
    }

    [Fact]
    public void SansLanceur_SurveilleLeParentDirect()
    {
        McpProcessEntry[] entries = [new(2, 1, "DockPad.exe", Started.AddSeconds(1)), new(1, 0, "codex.exe", Started)];

        var selected = McpClientProcess.SelectClient(2, entries);

        Assert.Equal(McpClientState.Running, selected.State);
        Assert.Equal(entries[1], selected.Client);
    }

    [Theory]
    [InlineData("claude.exe")]
    [InlineData("DockPad.exe")]
    public void PidReutilise_LeClientEstDejaMort(string executable)
    {
        McpProcessEntry[] entries = [new(3, 2, "DockPad.exe", Started.AddSeconds(2)),
            new(2, 1, "DockPad.exe", Started.AddSeconds(1)), new(1, 0, executable, Started.AddSeconds(3))];

        var selected = McpClientProcess.SelectClient(3, entries);

        Assert.Equal(McpClientState.Exited, selected.State);
        Assert.Null(selected.Client);
    }

    [Fact]
    public void ParentIntrouvable_NeSurveilleRien()
    {
        var selected = McpClientProcess.SelectClient(2, [new(2, 1, "DockPad.exe", Started)]);

        Assert.Equal(McpClientState.Unknown, selected.State);
        Assert.Null(selected.Client);
    }

    [Fact]
    public void AscendanceCyclique_NeSurveilleRien()
    {
        var selected = McpClientProcess.SelectClient(2,
            [new(2, 1, "DockPad.exe", Started), new(1, 2, "DockPad.exe", Started)]);

        Assert.Equal(McpClientState.Unknown, selected.State);
    }
}
