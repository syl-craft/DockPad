using System.Text.Json;
using System.Text.Json.Serialization;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Une valeur de rendu inconnue revient à Colonnes sans empêcher la lecture des autres réglages.
/// </summary>
public sealed class SideStripStyleJsonConverter : JsonConverter<SideStripStyle>
{
    public override SideStripStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number)
            && Enum.IsDefined((SideStripStyle)number))
            return (SideStripStyle)number;

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString()?.Trim();
            var name = Enum.GetNames<SideStripStyle>()
                .Where(candidate => string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (name != null)
                return Enum.Parse<SideStripStyle>(name);
        }

        reader.Skip();
        return SideStripStyle.Columns;
    }

    public override void Write(Utf8JsonWriter writer, SideStripStyle value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((int)(Enum.IsDefined(value) ? value : SideStripStyle.Columns));
}
