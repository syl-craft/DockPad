using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DockPad.Mcp;

/// <summary>
/// Mode --mcp : serveur MCP stdio (lancé par Claude Code / Claude Desktop).
/// Pur adaptateur : chaque outil est relayé à l'instance principale via DockPad_McpPipe.
/// stdout est réservé au protocole JSON-RPC — aucun logger console.
/// </summary>
public static class McpRelay
{
    public static async Task RunAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders(); // rien sur stdout/stderr — Serilog fichier reste dispo via LogService

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }
}
