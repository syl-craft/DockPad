using System.Globalization;
using System.IO;
using DockPad.Services.Localization;
using DockPad.Services.UpdateHost;
using DockPad.Services.Updates;

namespace DockPad.Tests;

public class UpdateInjectionTests
{
    [Fact]
    public void BackendsDefaultToForcedShutdown()
    {
        IUpdateBackend backend = new StandardBackend();
        using var service = new UpdateService(backend);
        Assert.True(backend.ForcesShutdown);
        Assert.True(service.ForcesShutdown);
        Assert.True(new VelopackBackendOptions("unused").ForcesShutdown);
        Assert.False(DockPadUpdates.BackendOptions.ForcesShutdown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ServiceRelaysTheBackendShutdownPolicy(bool forcesShutdown)
    {
        using var service = new UpdateService(new FakeUpdateBackend { ForcesShutdown = forcesShutdown });
        Assert.Equal(forcesShutdown, service.ForcesShutdown);
    }

    [Fact]
    public void DefaultClassifierHasNoApplicationExclusionsOrRelays()
    {
        var classifier = new ProgramClassifier();
        var result = classifier.Classify(new(@"C:\app\DockPad.exe", @"C:\app", "DockPad.exe --mcp",
            "claude.exe", @"C:\bin\claude.exe"));
        Assert.False(result.IsRelay);
        Assert.Equal("DockPad", result.Program);
        Assert.False(classifier.IsIgnored(@"C:\outside\DockPad.exe", @"C:\app"));
        Assert.False(classifier.IsIgnored(null, @"C:\app"));
    }

    [Fact]
    public async Task SearchPermissionIsReevaluatedBeforeEveryManualCheck()
    {
        var allowed = false;
        var backend = new FakeUpdateBackend();
        var schedule = new MemoryUpdateSchedule();
        var time = new ManualTime();
        using var service = new UpdateService(backend, new()
        {
            CanSearch = () => allowed, Schedule = schedule, Time = time,
        });

        await service.CheckAsync();
        Assert.Equal(0, backend.Checks);
        Assert.Equal(default, schedule.LastCheck);
        Assert.Equal(UpdateState.Idle, service.State);

        allowed = true;
        await service.CheckAsync();
        Assert.Equal(1, backend.Checks);
        Assert.Equal(time.GetUtcNow(), schedule.LastCheck);

        allowed = false;
        time.Advance(TimeSpan.FromHours(1));
        await service.CheckAsync();
        Assert.Equal(1, backend.Checks);
        Assert.Equal(time.GetUtcNow().AddHours(-1), schedule.LastCheck);
    }

    [Fact]
    public async Task PollingUsesInjectedTimeAndTheSixHourBoundary()
    {
        var time = new ManualTime();
        var backend = new FakeUpdateBackend();
        var schedule = new MemoryUpdateSchedule();
        schedule.RecordCheck(time.GetUtcNow().AddHours(-6).AddSeconds(20));
        using var service = new UpdateService(backend, new() { Schedule = schedule, Time = time });
        service.StartPolling();
        Assert.Equal(TimeSpan.FromSeconds(20), time.LastDelay);

        time.Advance(TimeSpan.FromSeconds(19));
        Assert.Equal(0, backend.Checks);
        time.Advance(TimeSpan.FromSeconds(1));
        await Until(() => time.TimerCount == 2);
        Assert.Equal(1, backend.Checks);
        Assert.Equal(time.GetUtcNow(), schedule.LastCheck);
        Assert.Equal(TimeSpan.FromMinutes(15), time.LastDelay);

        time.Advance(TimeSpan.FromMinutes(15));
        await Until(() => time.TimerCount == 3);
        Assert.Equal(1, backend.Checks);
        for (var cycle = 4; cycle <= 26; cycle++)
        {
            time.Advance(TimeSpan.FromMinutes(15));
            await Until(() => time.TimerCount == cycle);
        }
        Assert.Equal(2, backend.Checks);
        Assert.Equal(time.GetUtcNow(), schedule.LastCheck);
    }

    [Fact]
    public async Task PollingRechecksPermissionAndAutomaticOnEachCycle()
    {
        var time = new ManualTime();
        var backend = new FakeUpdateBackend();
        var schedule = new MemoryUpdateSchedule();
        var allowed = false;
        var permissionChecks = 0;
        using var service = new UpdateService(backend, new()
        {
            Schedule = schedule, Time = time, CanSearch = () => { permissionChecks++; return allowed; },
        });
        service.StartPolling();
        time.Advance(TimeSpan.FromSeconds(20));
        await Until(() => time.TimerCount == 2);
        Assert.Equal(1, permissionChecks);
        Assert.Equal(0, backend.Checks);

        allowed = true;
        service.Automatic = false;
        Assert.False(schedule.Automatic);
        time.Advance(TimeSpan.FromMinutes(15));
        await Until(() => time.TimerCount == 3);
        Assert.Equal(2, permissionChecks);
        Assert.Equal(0, backend.Checks);

        service.Automatic = true;
        time.Advance(TimeSpan.FromMinutes(15));
        await Until(() => time.TimerCount == 4);
        Assert.Equal(1, backend.Checks);
        Assert.Equal(4, permissionChecks);
        service.Dispose();
        time.Advance(TimeSpan.FromHours(7));
        Assert.Equal(1, backend.Checks);
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

    private sealed class StandardBackend : IUpdateBackend
    {
        public bool IsInstalled => false;
        public string CurrentVersion => "1.0.0";
        public string ContentDirectory => "";
        public Task<UpdateRelease?> CheckAsync() => throw new NotSupportedException();
        public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token) => throw new NotSupportedException();
        public void Apply(UpdateRelease release) => throw new NotSupportedException();
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(1, timeout.Token);
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