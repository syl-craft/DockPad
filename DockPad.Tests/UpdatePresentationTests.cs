using DockPad.Services.Localization;
using Mue;
using System.Globalization;

namespace DockPad.Tests;

public class UpdatePresentationTests
{
    [Theory]
    [InlineData(UpdateState.Idle)]
    [InlineData(UpdateState.Available)]
    [InlineData(UpdateState.Downloading)]
    [InlineData(UpdateState.Ready)]
    [InlineData(UpdateState.Failed)]
    [InlineData(UpdateState.Unavailable)]
    [InlineData(UpdateState.Checking)]
    [InlineData(UpdateState.Preparing)]
    [InlineData(UpdateState.Installing)]
    public void EachStateHasTranslatedLabels(UpdateState state)
    {
        var presentation = UpdatePresentation.For(state, hasChecked: true);
        Assert.NotEqual(presentation.LabelKey, Loc.T(presentation.LabelKey));
        Assert.NotEqual(presentation.TitleKey, Loc.F(presentation.TitleKey, "1.32.0"));
        Assert.NotEqual(presentation.SecondaryKey, Loc.T(presentation.SecondaryKey));
        Assert.NotEqual(presentation.PrimaryKey, Loc.T(presentation.PrimaryKey));
    }

    [Theory]
    [InlineData(0, "Update_CheckedNow", 0)]
    [InlineData(12, "Update_CheckedMinutes", 12)]
    [InlineData(120, "Update_CheckedHours", 2)]
    [InlineData(2880, "Update_CheckedDays", 2)]
    public void LastCheckUsesElapsedTimeAndTheCurrentCulture(int minutes, string key, int value)
    {
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(Loc.F(key, value), UpdateService.FormatLastChecked(now.AddMinutes(-minutes), now));
        Assert.Equal(Loc.T("Update_NeverChecked"), UpdateService.FormatLastChecked(default, now));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("qps-Ploc")]
    public async Task ReleaseMetadataUsesTranslatedUnitsAndTheIndicatorOnlyNamesVersions(string language)
    {
        var previous = Loc.Current;
        try
        {
            Loc.SetCulture(CultureInfo.GetCultureInfo(language));
            DateTimeOffset? checkedAt = null;
            using var service = new UpdateService(new PresentationBackend(size: 7_700_000), new UpdateServiceOptions { Schedule = new RecordingSchedule(time => checkedAt = time) });
            Assert.Equal("v1.31.1", service.VersionIndicator);
            await service.CheckAsync();

            Assert.NotNull(checkedAt);
            Assert.Equal("v1.31.1 → 1.32.0", service.VersionIndicator);
            Assert.Equal(new DateOnly(2026, 11, 1), service.ParsedNotes.Date);
            Assert.Contains(Loc.F("Update_Published", new DateOnly(2026, 11, 1).ToString("d", Loc.Formatting)), service.ReleaseMetadata);
            Assert.Contains(Loc.F("Update_Size", 7.7), service.ReleaseMetadata);
            Assert.Empty(service.DownloadSize);
        }
        finally { Loc.SetCulture(previous); }
    }

    [Fact]
    public async Task MissingSizeDoesNotInventAnEstimate()
    {
        using var service = new UpdateService(new PresentationBackend(size: null));
        await service.CheckAsync();
        Assert.DoesNotContain(Loc.F("Update_Size", 0), service.ReleaseMetadata);
        Assert.Empty(service.DownloadSize);
        Assert.Null(service.Release?.Size);
    }

    [Theory]
    [InlineData("fr", 1, "Fermer 1 programme et installer")]
    [InlineData("fr", 3, "Fermer 3 programmes et installer")]
    [InlineData("en", 1, "Close 1 program and install")]
    [InlineData("en", 3, "Close 3 programs and install")]
    public void CloseCountUsesTheLocalizedPlural(string language, int count, string expected)
    {
        var previous = Loc.Current;
        try
        {
            Loc.SetCulture(CultureInfo.GetCultureInfo(language));
            Assert.Equal(expected, Loc.F("Update_CloseCount", count));
        }
        finally { Loc.SetCulture(previous); }
    }

    private sealed class RecordingSchedule(Action<DateTimeOffset> record) : IUpdateSchedule
    {
        public bool Automatic { get; set; }
        public DateTimeOffset LastCheck { get; private set; }
        public void RecordCheck(DateTimeOffset at) { LastCheck = at; record(at); }
    }

    private sealed class PresentationBackend(long? size) : IUpdateBackend
    {
        public bool IsInstalled => true;
        public string CurrentVersion => "1.31.1";
        public string ContentDirectory => "unused";
        public Task<UpdateRelease?> CheckAsync() => Task.FromResult<UpdateRelease?>(
            new("1.32.0", "## [1.32.0] — 2026-11-01\n- **Change**", new object(), size));
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token) => throw new NotSupportedException();
        public void Apply(UpdateRelease release) => throw new NotSupportedException();
    }
}
