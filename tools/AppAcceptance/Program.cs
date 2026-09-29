using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

if (args.Length != 2) throw new ArgumentException("fixture-root startup-hook-dll");
string root = Path.GetFullPath(args[0]);
using var versions = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "versions.json")));
string baseVersion = versions.RootElement.GetProperty("Base").GetString()!;
string nextVersion = versions.RootElement.GetProperty("Next").GetString()!;
Environment.SetEnvironmentVariable("DOCKPAD_APP_ACCEPTANCE", root);
Environment.SetEnvironmentVariable("DOCKPAD_PROFILE_DIR", Path.Combine(root, "profile"));
Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", Path.GetFullPath(args[1]));
string desktopName = "DockPadAcceptance_" + Guid.NewGuid().ToString("N");
var desktop = Native.CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
if (desktop == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
var start = new Native.StartupInfo { cb = Marshal.SizeOf<Native.StartupInfo>(), desktop = desktopName };
var exe = Path.Combine(root, "app", "DockPad.exe");
if (!Native.CreateProcess(null, new StringBuilder('"' + exe + '"'), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, root, ref start, out var process)) throw new System.ComponentModel.Win32Exception();
Native.CloseHandle(process.thread); Native.CloseHandle(process.process);
try
{
    await Wait(() => Read("ready")?.GetProperty("version").GetString() == baseVersion, "initial app startup");
    var beforeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "profile", "sentinel.json"))));
    await Verify(baseVersion);
    Command("update");
    await Wait(() => Read("ready")?.GetProperty("version").GetString() == nextVersion, "actual DockPad update/restart");
    if (Read("ui-passed") is not { } ui || ui.GetArrayLength() < 30) throw new IOException("UI scenarios did not complete");
    Console.WriteLine($"PASS: {ui.GetArrayLength()} WPF UI assertions and checkpoints, real update through dialog buttons.");
    VerifyExit(baseVersion);
    await Verify(nextVersion);
    var updatedUi = Command("verify-updated-ui");
    await Wait(() => Read(updatedUi) is not null, "updated UI check");
    // The UI test deliberately toggles a persisted preference. Compare the profile
    // immediately before installation with the restarted app, not before that edit.
    var configHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "ui-profile.json")))!;
    foreach (var config in configHashes)
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "profile", config.Key)))) != config.Value)
            throw new IOException("Configuration changed: " + config.Key);
    Command("exit");
    await Wait(() => Read("exit-" + nextVersion) is not null, "clean exit");
    VerifyExit(nextVersion);
    var afterHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "profile", "sentinel.json"))));
    if (afterHash != beforeHash) throw new IOException("Profile sentinel changed");
    Console.WriteLine("PASS: actual DockPad N -> N+1, restart, hotkey registration/handler, HTTP/HTTPS commands, auto-start command, URL and injection dialogs, MCP pipe, profile, clean mutex/hotkey/pipe release.");
}
finally
{
    try
    {
        if (Read("ready") is { } state)
        {
            using var child = Process.GetProcessById(state.GetProperty("pid").GetInt32());
            if (child.MainModule?.FileName is { } image && image.StartsWith(Path.Combine(root, "app") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Command("exit");
                if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(5000); }
            }
        }
    }
    catch (ArgumentException) { }
    catch (InvalidOperationException) { }
    Native.CloseDesktop(desktop);
}

async Task Verify(string version)
{
    var hide = Command("hide"); await Wait(() => Read(hide) is not null, "hide");
    var hotkey = Command("hotkey"); await Wait(() => Read(hotkey) is not null, "hotkey");
    var result = Read(hotkey)!.Value;
    if (!result.GetProperty("registered").GetBoolean() || !result.GetProperty("visible").GetBoolean()) throw new IOException("Hotkey registration/handler failed");
    await Pipe("Url", "https://example.test/nin80");
    await Wait(() => Read("windows")?.EnumerateArray().Any(w => w.GetString() == "BrowserPickerWindow") == true, "URL picker");
    var close = Command("close-popups"); await Wait(() => Read(close) is not null, "URL popup close");
    await Pipe("Inject", Path.Combine(root, "fixture.env"));
    await Wait(() => Read("windows")?.EnumerateArray().Any(w => w.GetString() == "SecretInjectionWindow") == true, "secret injection dialog");
    close = Command("close-popups"); await Wait(() => Read(close) is not null, "injection popup close");
    var response = await Pipe("Mcp", "{\"tool\":\"dockpad_grid_get\",\"args\":{}}");
    if (!JsonDocument.Parse(response).RootElement.GetProperty("ok").GetBoolean()) throw new IOException("MCP failed: " + response);
    using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
    if ((string?)run?.GetValue("DockPadNIN80") != '"' + exe + '"') throw new IOException("Auto-start command unstable");
    using var url = Registry.CurrentUser.OpenSubKey(@"Software\Classes\DockPadNIN80URL\shell\open\command");
    if ((string?)url?.GetValue(null) != '"' + exe + "\" --url \"%1\"") throw new IOException("URL command unstable");
    using var assoc = Registry.CurrentUser.OpenSubKey(@"Software\DockPadNIN80\Capabilities\URLAssociations");
    if ((string?)assoc?.GetValue("http") != "DockPadNIN80URL" || (string?)assoc?.GetValue("https") != "DockPadNIN80URL") throw new IOException("HTTP/HTTPS registration missing");
    using var injection = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\DockPadNIN80InjectSecrets\command");
    if ((string?)injection?.GetValue(null) != '"' + exe + "\" --inject-secrets \"%1\"") throw new IOException("Injection command unstable");
    Console.WriteLine(version + ": integrations OK");
}
void VerifyExit(string version)
{
    var exit = Read("exit-" + version) ?? throw new IOException("No exit evidence");
    if (!exit.GetProperty("mutexFree").GetBoolean() || !exit.GetProperty("hotkeyFree").GetBoolean() || exit.GetProperty("free").GetArrayLength() != 4) throw new IOException("Resources still owned during Exit event: " + exit);
}
string Command(string op)
{
    string id = Guid.NewGuid().ToString("N");
    File.WriteAllText(Path.Combine(root, "command.tmp"), JsonSerializer.Serialize(new { id, op }));
    File.Move(Path.Combine(root, "command.tmp"), Path.Combine(root, "command.json"), true);
    return id;
}
JsonElement? Read(string name)
{
    var path = Path.Combine(root, name + ".json");
    if (!File.Exists(path)) return null;
    // Evidence is atomically replaced by the hook while this reader polls it.
    // Allow rename/delete on Windows instead of intermittently locking its writer.
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var document = JsonDocument.Parse(stream);
    return document.RootElement.Clone();
}
async Task Wait(Func<bool> condition, string what)
{
    for (int i = 0; i < 1800; i++)
    {
        if (File.Exists(Path.Combine(root, "error.txt"))) throw new IOException(File.ReadAllText(Path.Combine(root, "error.txt")));
        if (condition()) return;
        await Task.Delay(100);
    }
    throw new TimeoutException(what);
}
async Task<string> Pipe(string suffix, string message)
{
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var pipe = new NamedPipeClientStream(".", "DockPad_NIN80_Bench" + suffix, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(stop.Token);
    await pipe.WriteAsync(Encoding.UTF8.GetBytes(message + "\n"), stop.Token);
    using var reader = new StreamReader(pipe);
    return await reader.ReadLineAsync(stop.Token) ?? throw new IOException("No response");
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct StartupInfo
    {
        public int cb; public string? reserved, desktop, title; public int x, y, width, height, charsX, charsY, fill, flags; public short show, reserved2; public IntPtr reserved3, input, output, error;
    }
    [StructLayout(LayoutKind.Sequential)] public struct ProcessInfo { public IntPtr process, thread; public int pid, tid; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr attributes);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool CreateProcess(string? name, StringBuilder command, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInfo process);
}
