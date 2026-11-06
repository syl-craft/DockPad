using DockPad.Services.UpdateHost;
using DockPad.Services.Localization;
using DockPad.Services.Updates;

namespace DockPad.Tests;

public class UpdateBlockerGroupsTests
{
    [Theory]
    [InlineData(@"C:\App\current\DockPad.exe", "DockPad.exe --mcp", true)]
    [InlineData(@"C:\APP\current\DockPad.exe", "\"DockPad.exe\" \"--mcp\"", true)]
    [InlineData(@"C:\App\current-other\DockPad.exe", "DockPad.exe --mcp", false)]
    [InlineData(@"C:\Elsewhere\DockPad.exe", "DockPad.exe --mcp", false)]
    [InlineData(@"C:\App\current\DockPad.exe", "DockPad.exe --mcp-other", false)]
    [InlineData(@"C:\App\current\DockPad.exe", "DockPad.exe --MCP", false)]
    [InlineData(@"C:\App\current\DockPad.exe", "DockPad.exe \"description --mcp inside\"", false)]
    [InlineData(@"C:\App\current\DockPad.exe", "DockPad.exe \"description --mcp\"", false)]
    [InlineData(@"C:\App\current\DockPad.exe", "DockPad.exe \" --mcp \"", false)]
    [InlineData(@"C:\App\current\DockPad.exe", "DockPad.exe --url https://example.test/--mcp", false)]
    [InlineData(@"C:\App\current\DockPad.exe", null, false)]
    public void OnlyAnMcpArgumentInAnInstallationImageIdentifiesARelay(string image, string? command, bool expected)
    {
        var classification = new DockPadBlockerClassifier().Classify(new(image, @"C:\App\current", command, "claude.exe", @"C:\bin\claude.exe"));
        Assert.Equal(expected, classification.IsRelay);
    }

    [Theory]
    [InlineData("claude.exe", @"C:\bin\claude.exe", "Claude Code")]
    [InlineData("Claude.exe", @"C:\Users\Demo\AnthropicClaude\app\Claude.exe", "Claude Desktop")]
    [InlineData("Claude.exe", @"C:\Programs\Claude Desktop\Claude.exe", "Claude Desktop")]
    [InlineData("Editor.exe", @"C:\Programs\Editor.exe", "Editor")]
    public void RelayOwnerUsesTheParentProgram(string name, string path, string expected)
    {
        Assert.Equal(expected, new DockPadBlockerClassifier().Classify(new(@"C:\App\DockPad.exe", @"C:\App", "DockPad.exe --mcp", name, path)).Program);
    }

    [Fact]
    public void MissingParentIsExplicitlyUnknown()
    {
        var result = new DockPadBlockerClassifier().Classify(new(@"C:\App\DockPad.exe", @"C:\App", "DockPad.exe --mcp", null, null));
        Assert.True(result.IsRelay);
        Assert.Null(result.Program);
        Assert.Equal(Loc.T("Update_UnknownProgram"), new UpdateBlocker { IsRelay = true }.DisplayName);
    }

    [Theory]
    [InlineData(@"C:\App\DockPad.exe")]
    [InlineData(@"C:\Other\DockPad.exe")]
    public void StableLauncherIsSkippedWhenResolvingTheRelayOwner(string launcher)
    {
        var result = new DockPadBlockerClassifier().Classify(new(@"C:\App\current\DockPad.exe", @"C:\App\current",
            "DockPad.exe --mcp", "DockPad.exe", launcher, "claude.exe", @"C:\bin\claude.exe"));
        Assert.Equal("Claude Code", result.Program);
    }

    [Fact]
    public void DockPadInsideCurrentIsNotSkippedAsAParent()
    {
        var result = new DockPadBlockerClassifier().Classify(new(@"C:\App\current\DockPad.exe", @"C:\App\current",
            "DockPad.exe --mcp", "DockPad.exe", @"C:\App\current\DockPad.exe", "claude.exe", @"C:\bin\claude.exe"));
        Assert.Equal("DockPad", result.Program);
    }

    [Theory]
    [InlineData(@"C:\App\DockPad.exe", true)]
    [InlineData(@"C:\Other\DockPad.exe", true)]
    [InlineData(@"C:\App\current\DockPad.exe", false)]
    [InlineData(@"C:\Programs\Editor.exe", false)]
    [InlineData(null, false)]
    public void StableLauncherOutsideCurrentIsNotAnIndependentBlocker(string? image, bool expected) =>
        Assert.Equal(expected, new DockPadBlockerClassifier().IsIgnored(image, @"C:\App\current"));

    [Fact]
    public void WmiFailuresDiscardPartialMetadataInsteadOfBlockingInstallation()
    {
        Exception[] errors = [new System.Runtime.InteropServices.COMException("WMI unavailable"),
            new System.Management.ManagementException("WMI failed"), new UnauthorizedAccessException(), new InvalidOperationException()];
        foreach (var error in errors)
        {
            var metadata = new WindowsUpdateBlockerFinder().ReadProcessMetadata(partial =>
            {
                partial[1] = new(2, "DockPad.exe", null, "--mcp");
                throw error;
            });
            Assert.Empty(metadata);
        }
    }

    [Theory]
    [InlineData("fr", 0, "09:12")]
    [InlineData("fr", 1, "hier 09:12")]
    [InlineData("en-US", 0, "9:12 AM")]
    [InlineData("en-US", 1, "yesterday 9:12 AM")]
    public void StartedTimeDistinguishesTodayAndYesterday(string language, int daysAgo, string expected)
    {
        var previous = Loc.Current;
        try
        {
            Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo(language));
            var now = new DateTime(2026, 11, 1, 12, 0, 0);
            Assert.Equal(expected, UpdateBlocker.FormatStarted(now.Date.AddDays(-daysAgo).AddHours(9).AddMinutes(12), now));
            Assert.Equal(now.AddDays(-2).ToString("d", Loc.Formatting), UpdateBlocker.FormatStarted(now.AddDays(-2), now));
        }
        finally { Loc.SetCulture(previous); }
    }

    [Fact]
    public void RelaysAreGroupedFirstAndProgramsAreGroupedByNameWithoutSelectingThem()
    {
        UpdateBlocker[] processes = [
            new() { Pid = 1, IsRelay = true, ParentName = "Claude Code", Selected = true },
            new() { Pid = 2, IsRelay = true, ParentName = "Claude Desktop", Selected = true },
            new() { Pid = 3, Name = "Editor" },
            new() { Pid = 4, Name = "editor" },
            new() { Pid = 5, Name = "Terminal" },
        ];
        var groups = UpdateBlockerGroups.Group(processes);
        Assert.Equal(3, groups.Count);
        Assert.True(groups[0].IsRelay);
        Assert.Equal(2, groups[0].Count);
        Assert.True(groups[0].Selected);
        Assert.False(groups[1].Selected);
        Assert.Equal(2, groups[1].Count);
        groups[1].Selected = true;
        Assert.True(processes[2].Selected && processes[3].Selected);
        Assert.False(processes[4].Selected);
        groups[0].Selected = false;
        Assert.False(processes[0].Selected || processes[1].Selected);
    }

    [Fact]
    public void ReusedPidCannotMatchAStopRequestForEitherKindOfProcess()
    {
        foreach (var relay in new[] { false, true })
        {
            var blocker = new UpdateBlocker { Pid = 42, Started = 100, IsRelay = relay };
            Assert.False(UpdateBlockers.MatchesIdentity(blocker, started: 200));
            Assert.True(UpdateBlockers.MatchesIdentity(blocker, started: 100));
        }
    }

    [Fact]
    public void OnlyRelaysAreSelectedByDefault()
    {
        Assert.True(new UpdateBlocker { IsRelay = true }.Selected);
        Assert.False(new UpdateBlocker { IsRelay = false }.Selected);
    }
}
