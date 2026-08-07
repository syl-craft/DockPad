using System.Collections.ObjectModel;

namespace DockPad.Services;

public enum McpLogStatus { Success, Refused, Error }

public class McpLogEntry
{
    public DateTime Timestamp { get; init; }
    public string Tool { get; init; } = "";
    public string ParamsSummary { get; init; } = "";
    public McpLogStatus Status { get; init; }
    public string? Message { get; init; }

    public string StatusIcon => Status switch
    {
        McpLogStatus.Success => "✅",
        McpLogStatus.Refused => "🚫",
        _ => "❌",
    };
    public string TimeText => Timestamp.ToString("HH:mm:ss");
}

/// <summary>Journal en mémoire des actions MCP de la session (onglet Journal de McpConfigDialog).</summary>
public static class McpLogService
{
    public static ObservableCollection<McpLogEntry> Entries { get; } = [];

    public static void Add(string tool, string paramsSummary, McpLogStatus status, string? message = null)
    {
        var entry = new McpLogEntry
        {
            Timestamp = DateTime.Now, Tool = tool, ParamsSummary = paramsSummary,
            Status = status, Message = message,
        };

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Entries.Insert(0, entry);
        else dispatcher.BeginInvoke(() => Entries.Insert(0, entry));

        LogService.Info($"MCP {tool}({paramsSummary}) → {status}{(message is null ? "" : $" : {message}")}");
    }

    public static void Clear()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Entries.Clear();
        else dispatcher.BeginInvoke(Entries.Clear);
    }
}
