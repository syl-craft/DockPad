using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DockPad.Tests;

public class McpRelayLifetimeTests
{
    [Fact]
    public async Task MortDuClient_TermineLeRelaisMemeAvecStdinOuvert()
    {
        var profile = Path.Combine(Path.GetTempPath(), "dockpad-mcp-test-" + Guid.NewGuid());
        var executable = Path.Combine(AppContext.BaseDirectory, "DockPad.exe").Replace("'", "''");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        // Le relais hérite du tube dont le test garde l'écriture : la mort du client ne produit aucun EOF.
        // Une redirection force .NET à transmettre aussi les handles stdin/stdout au programme WinExe sans console.
        start.ArgumentList.Add("$start = [Diagnostics.ProcessStartInfo]::new('" + executable
            + "', '--mcp'); $start.UseShellExecute = $false; $start.CreateNoWindow = $true; "
            + "$start.RedirectStandardError = $true; "
            + "$relay = [Diagnostics.Process]::Start($start); [Console]::WriteLine($relay.Id); "
            + "[Threading.Thread]::Sleep(-1)");
        start.Environment["DOCKPAD_PROFILE_DIR"] = profile;
        using var client = Process.Start(start) ?? throw new InvalidOperationException("Cannot start MCP client");
        Process? relay = null;
        try
        {
            var pid = await client.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.NotNull(pid);
            relay = Process.GetProcessById(int.Parse(pid));
            await InitializeAsync(client.StandardInput, client.StandardOutput);

            client.Kill();
            await client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await relay.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, relay.ExitCode);
            Assert.Contains("client disparu", string.Join("", Directory.GetFiles(Path.Combine(profile, "logs"))
                .Select(File.ReadAllText)));
        }
        finally
        {
            if (relay != null)
            {
                if (!relay.HasExited) { relay.Kill(); await relay.WaitForExitAsync(); }
                relay.Dispose();
            }
            if (!client.HasExited) { client.Kill(); await client.WaitForExitAsync(); }
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FermetureDeStdin_TermineLeRelais(bool initialize)
    {
        var profile = Path.Combine(Path.GetTempPath(), "dockpad-mcp-test-" + Guid.NewGuid());
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "DockPad.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("--mcp");
        start.Environment["DOCKPAD_PROFILE_DIR"] = profile;
        using var relay = Process.Start(start) ?? throw new InvalidOperationException("Cannot start MCP relay");
        var errors = relay.StandardError.ReadToEndAsync();
        try
        {
            if (initialize)
            {
                // Une vraie réponse prouve que le test ferme une session active, sans appeler l'instance résidente.
                await InitializeAsync(relay.StandardInput, relay.StandardOutput);
            }
            relay.StandardInput.Close();
            await relay.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, relay.ExitCode);
            Assert.Equal("", await errors);
        }
        finally
        {
            // Seul le processus créé par ce test peut être arrêté, jamais les autres relais de la machine.
            if (!relay.HasExited) { relay.Kill(); await relay.WaitForExitAsync(); }
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
    }

    private static async Task InitializeAsync(StreamWriter input, StreamReader output)
    {
        await input.WriteLineAsync("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"DockPad.Tests","version":"1"}}}
            """);
        await input.FlushAsync();
        var response = await output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.NotNull(response);
        using var json = JsonDocument.Parse(response);
        Assert.Equal(1, json.RootElement.GetProperty("id").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("result", out _));
    }
}
