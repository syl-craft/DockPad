using System.Threading.Tasks;

namespace DockPad.Services.Updates;

public static class PendingWrites
{
    private static readonly HashSet<Task> Active = [];
    public static void Track(Task task)
    {
        lock (Active) Active.Add(task);
        _ = task.ContinueWith(_ => { lock (Active) Active.Remove(task); }, TaskScheduler.Default);
    }
    public static Task CompleteAsync()
    {
        lock (Active) return Task.WhenAll(Active.ToArray());
    }
}
