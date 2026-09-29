using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Velopack;

// Test-only instrumentation on a separate Windows desktop; never included in app packages.
public static class StartupHook
{
    private static readonly string Root = Environment.GetEnvironmentVariable("DOCKPAD_APP_ACCEPTANCE")!;
    private static DispatcherTimer? _timer;
    private static string Version => Application.Current.GetType().Assembly.GetName().Version!.ToString(3);
    public static void Initialize() => _ = Task.Run(async () =>
    {
        while (Application.Current is null) await Task.Delay(50);
        var app = Application.Current;
        await app.Dispatcher.InvokeAsync(() =>
        {
            app.Exit += (_, _) =>
            {
                var free = new List<string>();
                foreach (var pipe in new[] { "Url", "Inject", "Show", "Mcp" })
                {
                    try { using var server = new NamedPipeServerStream("DockPad_NIN80_Bench" + pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.FirstPipeInstance); free.Add(pipe); }
                    catch (IOException) { }
                }
                bool mutexFree = !Mutex.TryOpenExisting("DockPad_NIN80_BenchMutex", out var mutex);
                mutex?.Dispose();
                bool hotkeyFree = RegisterHotKey(IntPtr.Zero, 19280, 6, 0x87);
                if (hotkeyFree) UnregisterHotKey(IntPtr.Zero, 19280);
                Write("exit-" + Version, new { free, mutexFree, hotkeyFree });
            };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            bool ready = false;
            _timer.Tick += async (_, _) =>
            {
                try
                {
                    if (app.MainWindow is not { } main) return;
                    if (!ready)
                    {
                        ready = true;
                        Write("ready", new { version = Version, pid = Environment.ProcessId });
                    }
                    Write("windows", app.Windows.Cast<Window>().Select(w => w.GetType().Name).ToArray());
                    var command = Path.Combine(Root, "command.json");
                    if (!File.Exists(command)) return;
                    using var json = JsonDocument.Parse(File.ReadAllText(command));
                    File.Delete(command);
                    string id = json.RootElement.GetProperty("id").GetString()!;
                    string op = json.RootElement.GetProperty("op").GetString()!;
                    if (op == "hide") { main.Hide(); Write(id, new { ok = true }); }
                    if (op == "hotkey")
                    {
                        bool available = RegisterHotKey(IntPtr.Zero, 19280, 6, 0x87);
                        if (available) UnregisterHotKey(IntPtr.Zero, 19280);
                        PostMessage(new WindowInteropHelper(main).Handle, 0x0312, new IntPtr(9001), IntPtr.Zero);
                        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                        Write(id, new { registered = !available, visible = main.IsVisible });
                    }
                    if (op == "close-popups")
                    {
                        foreach (var window in app.Windows.Cast<Window>().Where(w => w != main).ToArray()) window.Close();
                        Write(id, new { ok = true });
                    }
                    if (op == "exit") { _timer.Stop(); Exit(app); }
                    if (op == "verify-updated-ui") { await UpdateUiAcceptance.VerifyRestartAsync(Root); Write(id, new { ok = true }); }
                    if (op == "update")
                    {
                        _timer.Stop();
                        await UpdateUiAcceptance.RunAsync(Root);
                    }
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(Root, "error.txt"), ex.ToString()); }
            };
            _timer.Start();
        });
    });
    private static void Exit(Application app) => app.GetType().GetMethod("Exit", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.Invoke(null, null);
    private static void Write(string name, object value)
    {
        var target = Path.Combine(Root, name + ".json");
        File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(value));
        File.Move(target + ".tmp", target, true);
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
}
