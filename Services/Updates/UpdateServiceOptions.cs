namespace DockPad.Services.Updates;

public interface IUpdateLog
{
    void Warn(Exception ex, string context);
}

public sealed class SilentUpdateLog : IUpdateLog
{
    public void Warn(Exception ex, string context) { }
}

public interface IUpdateSchedule
{
    bool Automatic { get; set; }
    DateTimeOffset LastCheck { get; }
    void RecordCheck(DateTimeOffset at);
}

public sealed class MemoryUpdateSchedule : IUpdateSchedule
{
    public bool Automatic { get; set; } = true;
    public DateTimeOffset LastCheck { get; private set; }
    public void RecordCheck(DateTimeOffset at) => LastCheck = at;
}

public sealed record UpdateServiceOptions
{
    public IUpdateLog Log { get; init; } = new SilentUpdateLog();
    public IUpdateSchedule Schedule { get; init; } = new MemoryUpdateSchedule();
    public Func<bool> CanSearch { get; init; } = () => true;
    public TimeProvider Time { get; init; } = TimeProvider.System;
}
