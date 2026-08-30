namespace DockPad.Services;

/// <summary>Verrou global des load-modify-save de configs (UI et MCP sérialisés).</summary>
public static class ConfigLock
{
    public static readonly object Gate = new();
}
