using System.Globalization;
using System.IO;
using DockPad.Services.Localization;
using DockPad.Services.UpdateHost;
using Mue;

namespace DockPad.Tests;

public class UpdateInjectionTests
{
    [Fact]
    public void BackendsDefaultToForcedShutdown()
    {
        Assert.False(DockPadUpdates.BackendOptions.ForcesShutdown);
    }

    [Fact]
    public async Task InjectedScheduleRecordsFailedSearchAndFormatsLastChecked()
    {
        var previous = Loc.Current;
        try
        {
            Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
            var time = new ManualTime();
            var schedule = new MemoryUpdateSchedule();
            var log = new RecordingLog();
            var error = new IOException("offline");
            using var service = new UpdateService(new FakeUpdateBackend { CheckFailure = error }, new()
            {
                Schedule = schedule, Time = time, Log = log,
            });
            await service.CheckAsync();
            Assert.Equal(UpdateState.Failed, service.State);
            Assert.Equal(time.GetUtcNow(), schedule.LastCheck);
            Assert.Same(error, Assert.Single(log.Errors));
            time.Advance(TimeSpan.FromMinutes(12));
            Assert.Equal(Loc.F("Update_CheckedMinutes", 12), service.LastChecked);
        }
        finally { Loc.SetCulture(previous); }
    }

    [Fact]
    public void ModelGettersUseLocalizationBeforeTheApplicationStarts()
    {
        var previous = Loc.Current;
        try
        {
            var blocker = new UpdateBlocker { IsRelay = true };
            using var service = new UpdateService(new FakeUpdateBackend());
            var notifications = 0;
            service.PropertyChanged += (_, _) => notifications++;
            Loc.SetCulture(CultureInfo.GetCultureInfo("en"));
            Assert.Equal(Loc.T("Update_UnknownProgram"), blocker.DisplayName);
            Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
            Assert.Equal(Loc.T("Update_UnknownProgram"), blocker.DisplayName);
            Assert.True(notifications > 0);
            Assert.Equal(Loc.Formatting, UpdateTexts.Formatting);
        }
        finally { Loc.SetCulture(previous); }
    }

    private sealed class RecordingLog : IUpdateLog
    {
        public List<Exception> Errors { get; } = [];
        public void Warn(Exception ex, string context) => Errors.Add(ex);
    }

    private sealed class ManualTime : TimeProvider
    {
        private readonly ManualTimeProvider _time = new();
        private readonly object _gate = new();
        private int _timerCount;
        private TimeSpan _lastDelay;
        public override DateTimeOffset GetUtcNow() => _time.GetUtcNow();
        public int TimerCount { get { lock (_gate) return _timerCount; } }
        public TimeSpan LastDelay { get { lock (_gate) return _lastDelay; } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                var timer = _time.CreateTimer(callback, state, dueTime, period);
                _lastDelay = dueTime;
                _timerCount++;
                return timer;
            }
        }
        public void Advance(TimeSpan elapsed) => _time.Advance(elapsed);
    }
}