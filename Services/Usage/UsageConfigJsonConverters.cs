using System.Text.Json;
using System.Text.Json.Serialization;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Un nom individuel défini : ni liste séparée par des virgules, ni nombre écrit en chaîne.
/// </summary>
public static class EnumNames
{
    public static bool TryParseSingleName<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        var name = Enum.GetNames<TEnum>()
            .Where(candidate => string.Equals(candidate, text?.Trim(), StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        value = name == null ? default : Enum.Parse<TEnum>(name);
        return name != null;
    }
}

/// <summary>
/// Enumération lue en chaîne ou en nombre connu ; toute autre forme donne le défaut sans empêcher la lecture des autres champs.
/// </summary>
public class TolerantEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private readonly TEnum _default;

    public TolerantEnumJsonConverter(TEnum defaultValue) => _default = defaultValue;

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && EnumNames.TryParseSingleName<TEnum>(reader.GetString(), out var named))
            return named;

        if (reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out var number)
            && Enum.IsDefined(typeof(TEnum), number))
            return (TEnum)Enum.ToObject(typeof(TEnum), number);

        reader.Skip();
        return _default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue((Enum.IsDefined(value) ? value : _default).ToString());
}

public sealed class UsagePlacementJsonConverter() : TolerantEnumJsonConverter<UsagePlacement>(UsagePlacement.Bottom);

public sealed class UsageFocusModeJsonConverter() : TolerantEnumJsonConverter<UsageFocusMode>(UsageFocusMode.Manual);

/// <summary>
/// Booléen lu tel quel ; toute autre forme JSON donne le défaut du champ.
/// </summary>
public class TolerantBoolJsonConverter : JsonConverter<bool>
{
    private readonly bool _default;

    public TolerantBoolJsonConverter(bool defaultValue) => _default = defaultValue;

    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
            return reader.GetBoolean();

        reader.Skip();
        return _default;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);
}

public sealed class TrueByDefaultBoolJsonConverter() : TolerantBoolJsonConverter(true);
