using System.Text.Json.Serialization;

namespace DockPad.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TileLayout { Simple, Quad, TwoPlusFour }

public readonly record struct TileAddress(int Page, int Row, int Col, int? Slot = null)
{
    public static TileAddress Of(ShortcutEntry entry) => new(entry.Page, entry.Row, entry.Col, entry.Slot);
    public TileAddress Root => this with { Slot = null };
}
