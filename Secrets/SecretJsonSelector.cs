using System.Text.Json;

namespace DockPad.Secrets;

/// <summary>Pourquoi une propriété JSON n'a rien rendu.</summary>
public enum SecretJsonFailure { None, InvalidJson, NoScalarValue }

/// <summary>
/// La valeur extraite, ou la raison de son absence — jamais un morceau du document.
/// </summary>
public readonly record struct SecretJsonSelection(string? Value, SecretJsonFailure Failure);

/// <summary>
/// Extrait une propriété d'un document JSON par un chemin pointé (<c>servers.0.host</c>).
/// </summary>
/// <remarks>
/// La seule transformation qu'un marqueur applique à une valeur du coffre. Le message d'une
/// <see cref="JsonException"/> n'est jamais repris : il peut citer le contenu.
/// </remarks>
public static class SecretJsonSelector
{
    public static SecretJsonSelection Select(string json, string path)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return new(null, SecretJsonFailure.InvalidJson); }

        using (document)
        {
            var current = document.RootElement;

            foreach (var segment in path.Split('.'))
            {
                if (TryStep(current, segment, out var next) == false)
                    return new(null, SecretJsonFailure.NoScalarValue);

                current = next;
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => new(current.GetString(), SecretJsonFailure.None),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                    new(current.GetRawText(), SecretJsonFailure.None),
                _ => new(null, SecretJsonFailure.NoScalarValue),
            };
        }
    }

    /// <summary>
    /// Une propriété dans un objet, un index en base 0 dans un tableau.
    /// </summary>
    private static bool TryStep(JsonElement current, string segment, out JsonElement next)
    {
        next = default;

        if (current.ValueKind == JsonValueKind.Object)
            return segment.Length > 0 && current.TryGetProperty(segment, out next);

        if (current.ValueKind == JsonValueKind.Array
            && int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            && index < current.GetArrayLength())
        {
            next = current[index];
            return true;
        }

        return false;
    }
}
