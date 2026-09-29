using System.Diagnostics.Tracing;

// Loaded only by the acceptance harness through DOTNET_STARTUP_HOOKS, never shipped with DockPad.
public static class StartupHook
{
    private static EventListener? _listener;
    public static void Initialize()
    {
        _listener = new NetworkEvents();
        NetworkEvents.Record("attached");
    }
    private sealed class NetworkEvents : EventListener
    {
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name is "System.Net.Http" or "System.Net.NameResolution" or "System.Net.Sockets")
                EnableEvents(source, EventLevel.Verbose);
        }
        protected override void OnEventWritten(EventWrittenEventArgs data) => Record(data.EventSource.Name + "/" + data.EventName);
        public static void Record(string text)
        {
            var directory = Environment.GetEnvironmentVariable("DOCKPAD_NETWORK_PROBE")!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, Environment.ProcessId + ".txt"), text + "\n");
        }
    }
}
