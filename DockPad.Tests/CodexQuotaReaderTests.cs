using System.Globalization;
using System.IO;
using System.Text.Json;
using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class CodexQuotaReaderTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "dockpad-codex-quota-" + Guid.NewGuid().ToString("N"));
    private readonly string? _savedHome = Environment.GetEnvironmentVariable(CodexUsageReader.HomeVariable);
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 14, 0, 0, TimeSpan.Zero);

    public CodexQuotaReaderTests() => Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, null);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CodexUsageReader.HomeVariable, _savedHome);
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private static object Window(double used, int minutes, DateTimeOffset? reset = null) => new
    {
        used_percent = used,
        window_minutes = minutes,
        resets_at = (reset ?? Now.AddHours(2)).ToUnixTimeSeconds(),
    };

    private static string Line(DateTimeOffset at, object? primary, object? secondary = null, string? limitId = "codex") =>
        JsonSerializer.Serialize(new
        {
            type = "event_msg", timestamp = at.ToString("O", CultureInfo.InvariantCulture),
            payload = new
            {
                type = "token_count", info = (object?)null,
                rate_limits = new { limit_id = limitId, primary, secondary },
            },
        });

    private string Write(string root, string name, params string[] lines)
    {
        var dir = Path.Combine(_home, ".codex", root);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "rollout-" + name + ".jsonl");
        File.WriteAllLines(path, lines);
        File.SetLastWriteTime(path, Now.LocalDateTime);
        return path;
    }

    [Fact]
    public void BothWindows_MapPercentAndUnixReset()
    {
        var quota = CodexQuotaReader.ParseLine(Line(Now, Window(12.5, 300), Window(42, 10080, Now.AddDays(3))));
        Assert.NotNull(quota);
        Assert.Equal(13, quota.Session!.UsedPct);
        Assert.Equal(42, quota.Week!.UsedPct);
        Assert.Equal(Now.AddDays(3).LocalDateTime, quota.Week.ResetsAt);
    }

    [Fact]
    public void WeeklyInPrimary_IsNotMislabelledAsSession()
    {
        var quota = CodexQuotaReader.ParseLine(Line(Now, Window(5, 10080)));
        Assert.NotNull(quota);
        Assert.Null(quota.Session);
        Assert.Equal(5, quota.Week!.UsedPct);
    }

    [Fact]
    public void ReversedWindows_FollowTheirDuration()
    {
        var quota = CodexQuotaReader.ParseLine(Line(Now, Window(5, 10080), Window(20, 300)));
        Assert.Equal(20, quota!.Session!.UsedPct);
        Assert.Equal(5, quota.Week!.UsedPct);
    }

    [Fact]
    public void ModelSpecificBucket_IsIgnored()
    {
        Assert.Null(CodexQuotaReader.ParseLine(Line(Now, Window(90, 300), limitId: "codex_other")));
        Assert.NotNull(CodexQuotaReader.ParseLine(Line(Now, Window(10, 300), limitId: null)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("{\"payload\":null}")]
    [InlineData("{\"payload\":{\"type\":42}}")]
    public void InvalidEvent_IsIgnored(string line) => Assert.Null(CodexQuotaReader.ParseLine(line));

    [Fact]
    public void InvalidWindow_DoesNotHideOtherValidWindow()
    {
        var invalid = new { used_percent = "bad", window_minutes = 300, resets_at = 123 };
        var quota = CodexQuotaReader.ParseLine(Line(Now, invalid, Window(50, 10080)));
        Assert.Null(quota!.Session);
        Assert.Equal(50, quota.Week!.UsedPct);
    }

    [Fact]
    public void UnknownDuration_IsNotInventedAsAWeekOrSession()
    {
        var quota = CodexQuotaReader.ParseLine(Line(Now, Window(30, 1440)));
        Assert.NotNull(quota);
        Assert.Null(quota.Session);
        Assert.Null(quota.Week);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(150, 100)]
    [InlineData(0, 0)]
    public void Percent_IsBounded(double source, int expected) =>
        Assert.Equal(expected, CodexQuotaReader.ParseLine(Line(Now, Window(source, 300)))!.Session!.UsedPct);

    [Fact]
    public void BadReset_IsIgnoredWithoutThrowing()
    {
        var invalid = new { used_percent = 12, window_minutes = 300, resets_at = long.MaxValue };
        Assert.Null(CodexQuotaReader.ParseLine(Line(Now, invalid))!.Session);
    }

    [Theory]
    [InlineData(-16)]
    [InlineData(1)]
    public void StaleOrFutureSnapshot_IsHidden(int minutes)
    {
        var quota = CodexQuotaReader.ParseLine(Line(Now.AddMinutes(minutes), Window(10, 300)));
        Assert.Null(CodexQuotaReader.Current(quota, Now.LocalDateTime));
    }

    [Fact]
    public void ExpiredSession_IsHiddenWhileWeekRemains()
    {
        var quota = CodexQuotaReader.ParseLine(Line(Now.AddMinutes(-1), Window(90, 300, Now), Window(5, 10080)));
        var current = CodexQuotaReader.Current(quota, Now.LocalDateTime);
        Assert.NotNull(current);
        Assert.Null(current.Session);
        Assert.Equal(5, current.Week!.UsedPct);
    }

    [Fact]
    public void Scan_ChoosesEventTimestampAcrossSessionsAndArchives()
    {
        var old = Write("sessions", "old", Line(Now.AddMinutes(-10), Window(1, 10080)));
        File.SetLastWriteTime(old, Now.AddHours(1).LocalDateTime);
        Write("archived_sessions", "new", Line(Now.AddMinutes(-1), Window(6, 10080)),
            Line(Now, Window(90, 300), limitId: "codex_other"));
        var snapshot = CodexUsageReader.ReadSnapshot(_home, Now.AddDays(-1).LocalDateTime);
        Assert.Equal(6, snapshot.Quota!.Week!.UsedPct);
        Assert.Empty(snapshot.Entries); // info:null porte un quota, pas une consommation de jetons
    }

    [Fact]
    public async Task QuotaOnlyEvent_DoesNotPreventReadingLaterTokenUsage()
    {
        Write("sessions", "mixed", Line(Now, Window(5, 10080)),
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null}}",
            JsonSerializer.Serialize(new
            {
                type = "event_msg", timestamp = Now.ToString("O"),
                payload = new { type = "token_count", info = new { last_token_usage = new { input_tokens = 100, output_tokens = 20 } } },
            }));
        var provider = new CodexUsageProvider(_home, () => Now.LocalDateTime);
        var usage = await provider.ReadAsync(CancellationToken.None);
        Assert.Equal(120, usage!.DayTokens);
        Assert.Equal(5, usage.Week!.UsedPct);
        Assert.Null(usage.Session);
        Assert.Empty(usage.QuotaNotice);
        Assert.Empty(usage.Cost);
    }

    [Fact]
    public async Task ViewModel_ShowsWeeklyPrimaryAndHidesAbsentSession()
    {
        Write("sessions", "weekly", Line(Now, Window(5, 10080)));
        var service = new UsageService([new CodexUsageProvider(_home, () => Now.LocalDateTime)]);
        var vm = new UsageViewModel(service, () => new UsageConfig(), () => Now.LocalDateTime);
        await vm.RefreshAsync();
        Assert.True(vm.IsVisible);
        Assert.True(vm.WeekGauge!.HasQuota);
        Assert.Equal(5, vm.WeekGauge.UsedPct);
        Assert.False(vm.SessionGauge!.HasQuota);
        Assert.False(vm.HasQuotaNotice);
    }

    [Fact]
    public async Task Provider_HidesStaleSnapshotAndExplainsWhy()
    {
        Write("sessions", "stale", Line(Now.AddMinutes(-16), Window(5, 10080)));
        var usage = await new CodexUsageProvider(_home, () => Now.LocalDateTime).ReadAsync(CancellationToken.None);
        Assert.NotNull(usage);
        Assert.Null(usage.Week);
        Assert.NotEmpty(usage.QuotaNotice);
        Assert.NotEmpty(usage.QuotaNoticeNote);
    }

    [Fact]
    public void NewEmptySnapshot_DoesNotResurrectOlderQuota()
    {
        Write("sessions", "cleared", Line(Now.AddMinutes(-1), Window(5, 10080)), Line(Now, null));
        var snapshot = CodexUsageReader.ReadSnapshot(_home, Now.AddDays(-1).LocalDateTime);
        Assert.Equal(Now, snapshot.Quota!.ObservedAt);
        Assert.Null(snapshot.Quota.Week);
    }
}
