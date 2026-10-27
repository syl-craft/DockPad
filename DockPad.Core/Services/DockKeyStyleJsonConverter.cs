using System.Text.Json;
using System.Text.Json.Serialization;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Un style de touches inconnu revient à Halo sans empêcher la lecture des autres réglages.
/// </summary>
public sealed class DockKeyStyleJsonConverter : JsonConverter<DockKeyStyle>
{
    public override DockKeyStyle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number)
            && Enum.IsDefined((DockKeyStyle)number))
            return (DockKeyStyle)number;

        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<DockKeyStyle>(reader.GetString(), ignoreCase: true, out var style)
            && Enum.IsDefined(style))
            return style;

        reader.Skip();
        return DockKeyStyle.Halo;
    }

    public override void Write(Utf8JsonWriter writer, DockKeyStyle value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((int)(Enum.IsDefined(value) ? value : DockKeyStyle.Halo));
}
