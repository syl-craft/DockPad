namespace DockPad.Models;

/// <summary>Résultat d'une action des services (UI ou MCP) : succès + données, ou échec + message.</summary>
public class ActionResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public object? Data { get; init; }

    public static ActionResult Success(object? data = null) => new() { Ok = true, Data = data };
    public static ActionResult Fail(string error) => new() { Ok = false, Error = error };
}
