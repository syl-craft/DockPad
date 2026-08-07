using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Traite une requête MCP reçue sur le pipe : options → service d'action → journal → réponse JSON.
/// Requête : {"tool":"dockpad_...","args":{...}} — Réponse : {"ok":bool,"data":...,"error":...}.
/// </summary>
public static class McpDispatcher
{
    /// <summary>Branché par App sur le rafraîchissement de la grille (Dispatcher).</summary>
    public static Action? OnMutation { get; set; }

    private static readonly HashSet<string> DeleteTools =
        ["dockpad_shortcut_delete", "dockpad_page_delete", "dockpad_rule_delete"];
    private static readonly HashSet<string> ReadTools =
        ["dockpad_grid_get", "dockpad_browser_list", "dockpad_rule_list"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Handle(string requestJson, McpConfig? configOverride = null)
    {
        string tool = "?", summary = "";
        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            tool = doc.RootElement.GetProperty("tool").GetString() ?? "?";
            JsonElement args = doc.RootElement.TryGetProperty("args", out var a) ? a.Clone() : default;
            summary = Summarize(args);

            var config = configOverride ?? McpConfigService.Load();
            if (!config.Enabled)
                return Refused(tool, summary, "Le serveur MCP est désactivé dans les options de DockPad.");
            if (DeleteTools.Contains(tool) && !config.AllowDelete)
                return Refused(tool, summary,
                    "La suppression via MCP est désactivée (fenêtre Serveur MCP → Options).");

            var result = Execute(tool, args);
            McpLogService.Add(tool, summary,
                result.Ok ? McpLogStatus.Success : McpLogStatus.Error, result.Error);
            if (result.Ok && !ReadTools.Contains(tool)) OnMutation?.Invoke();
            return Serialize(result);
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Requête MCP invalide ({tool})");
            McpLogService.Add(tool, summary, McpLogStatus.Error, ex.Message);
            return Serialize(ActionResult.Fail($"Requête invalide : {ex.Message}"));
        }
    }

    private static ActionResult Execute(string tool, JsonElement args) => tool switch
    {
        "dockpad_grid_get"        => ShortcutActionService.GetGrid(OptInt(args, "page")),
        "dockpad_shortcut_add"    => ShortcutActionService.Add(
                                         Deserialize<List<ShortcutAddItem>>(args, "items")
                                         ?? throw new JsonException("items requis")),
        "dockpad_shortcut_update" => ShortcutActionService.Update(
                                         ReqInt(args, "page"), ReqInt(args, "row"), ReqInt(args, "col"),
                                         Deserialize<ShortcutUpdate>(args, "changes") ?? new ShortcutUpdate()),
        "dockpad_shortcut_move"   => ShortcutActionService.Move(
                                         ReqInt(args, "page"), ReqInt(args, "row"), ReqInt(args, "col"),
                                         ReqInt(args, "toPage"), OptInt(args, "toRow"), OptInt(args, "toCol")),
        "dockpad_shortcut_delete" => ShortcutActionService.Delete(
                                         ReqInt(args, "page"), ReqInt(args, "row"), ReqInt(args, "col")),
        "dockpad_page_add"        => PageActionService.Add(OptString(args, "iconPath")),
        // iconPath : propriété absente = inchangé ; "" = retirer (mappé vers null) ; chemin = nouvelle icône
        "dockpad_page_update"     => PageActionService.Update(
                                         ReqInt(args, "index"),
                                         iconProvided: args.ValueKind == JsonValueKind.Object
                                                       && args.TryGetProperty("iconPath", out _),
                                         OptString(args, "iconPath") is { Length: > 0 } ip ? ip : null,
                                         OptInt(args, "newIndex")),
        "dockpad_page_delete"     => PageActionService.Delete(ReqInt(args, "index")),
        "dockpad_browser_list"    => BrowserActionService.ListBrowsers(),
        "dockpad_browser_update"  => BrowserActionService.UpdateBrowser(
                                         ReqString(args, "id"),
                                         Deserialize<BrowserUpdate>(args, "changes") ?? new BrowserUpdate()),
        "dockpad_rule_list"       => BrowserActionService.ListRules(),
        "dockpad_rule_add"        => BrowserActionService.AddRule(
                                         ReqString(args, "host"), ReqString(args, "browserId")),
        "dockpad_rule_delete"     => BrowserActionService.DeleteRule(ReqString(args, "host")),
        _ => ActionResult.Fail($"Outil inconnu : {tool}"),
    };

    // ───────────── Aides ─────────────

    private static string Serialize(ActionResult r) =>
        JsonSerializer.Serialize(new { ok = r.Ok, data = r.Data, error = r.Error }, JsonOpts);

    private static string Refused(string tool, string summary, string message)
    {
        McpLogService.Add(tool, summary, McpLogStatus.Refused, message);
        return Serialize(ActionResult.Fail(message));
    }

    private static string Summarize(JsonElement args) =>
        args.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? args.GetRawText() is { Length: > 120 } raw ? raw[..120] + "…" : args.GetRawText()
            : "";

    private static int ReqInt(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : throw new JsonException($"{name} (entier) requis");

    private static int? OptInt(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : null;

    private static string ReqString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : throw new JsonException($"{name} (chaîne) requis");

    private static string? OptString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static T? Deserialize<T>(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            ? v.Deserialize<T>(JsonOpts) : default;
}
