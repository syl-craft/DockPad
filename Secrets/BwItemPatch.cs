using System.Text;
using System.Text.Json.Nodes;

namespace DockPad.Secrets;

/// <summary>
/// PUR — le JSON qu'on donne à <c>bw create item</c> et à <c>bw edit item</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>bw edit item</c> remplace la fiche entière.</b> <see cref="BwItem"/> est volontairement
/// partiel, donc réécrire à partir de lui effacerait les adresses, les pièces jointes et
/// l'historique. On travaille donc sur la sortie <b>complète</b> de <c>bw get item</c>, en
/// <see cref="JsonNode"/>, et on ne touche qu'aux champs visés — une propriété inconnue du modèle
/// traverse intacte.
/// </para>
/// <para>
/// <b>Même ordre que <see cref="SecretFieldResolver"/></b> : un champ personnalisé du nom demandé
/// gagne sur le champ standard. Écrire ailleurs que là où la lecture cherche d'abord, c'est écrire
/// une valeur que la prochaine injection ne lirait pas.
/// </para>
/// </remarks>
public static class BwItemPatch
{
    /// <summary>Type Bitwarden d'un champ personnalisé masqué : la valeur n'apparaît pas en clair.</summary>
    public const int HiddenField = 1;

    private const int LoginItem = 1;

    /// <summary>Un item Identifiant neuf, rangé dans l'organisation et la collection données.</summary>
    public static string NewItem(
        string name, string? organisationId, string? collectionId, IReadOnlyList<SecretFieldValue> fields)
    {
        var item = new JsonObject
        {
            ["type"] = LoginItem,
            ["name"] = name,
            ["organizationId"] = organisationId,
            ["collectionIds"] = collectionId is null ? new JsonArray() : new JsonArray(collectionId),
            ["folderId"] = null,
            ["notes"] = null,
            ["favorite"] = false,
            ["reprompt"] = 0,
            ["login"] = new JsonObject { ["username"] = null, ["password"] = null, ["totp"] = null, ["uris"] = new JsonArray() },
            ["fields"] = new JsonArray(),
        };

        Apply(item, fields);
        return item.ToJsonString();
    }

    /// <summary>La fiche complète lue par <c>bw get item</c>, avec les champs en plus.</summary>
    public static string AddFields(string fullItemJson, IReadOnlyList<SecretFieldValue> fields)
    {
        var item = JsonNode.Parse(fullItemJson)!.AsObject();

        Apply(item, fields);
        return item.ToJsonString();
    }

    /// <summary>Ce que <c>bw create</c> et <c>bw edit</c> attendent sur l'entrée standard.</summary>
    public static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static void Apply(JsonObject item, IReadOnlyList<SecretFieldValue> fields)
    {
        foreach (var (field, value) in fields)
        {
            var custom = (item["fields"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(f =>
                string.Equals((string?)f["name"], field, StringComparison.OrdinalIgnoreCase));

            if (custom is not null) { custom["value"] = value; continue; }

            switch (field.ToLowerInvariant())
            {
                case "password" or "username" or "totp":
                    Login(item)[field.ToLowerInvariant()] = value;
                    break;
                case "notes":
                    item["notes"] = value;
                    break;
                default:
                    Fields(item).Add(new JsonObject { ["name"] = field, ["value"] = value, ["type"] = HiddenField });
                    break;
            }
        }
    }

    private static JsonObject Login(JsonObject item)
    {
        if (item["login"] is JsonObject login) return login;

        login = new JsonObject();
        item["login"] = login;
        return login;
    }

    private static JsonArray Fields(JsonObject item)
    {
        if (item["fields"] is JsonArray fields) return fields;

        fields = [];
        item["fields"] = fields;
        return fields;
    }
}
