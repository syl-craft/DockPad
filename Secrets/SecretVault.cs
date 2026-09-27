namespace DockPad.Secrets;

/// <summary>Ce que le coffre répond à un marqueur, avant toute valeur.</summary>
public enum SecretPresenceKind { Found, ItemMissing, FieldMissing, Ambiguous }

/// <summary>
/// La présence d'un marqueur, et l'item qui le porte quand il est unique.
/// </summary>
/// <remarks>
/// <see cref="SecretPresenceKind.Ambiguous"/> ne porte pas d'identifiant : écrire dans l'un des
/// deux au hasard serait pire que de ne rien proposer.
/// </remarks>
public readonly record struct SecretPresence(SecretPresenceKind Kind, string? ItemId);

/// <summary>
/// L'instantané du coffre, et la seule chose qui répond à un marqueur.
/// </summary>
/// <remarks>
/// <para>
/// Pur : construit à partir d'une liste d'items déjà lue, sans jamais toucher à la CLI. C'est ce
/// qui permet de vérifier les trois cas d'échec — item absent, item en double, champ vide — sans
/// coffre ni réseau.
/// </para>
/// <para>
/// <b>Un seul <c>bw list items</c> alimente tout.</b> Le script d'origine lançait une recherche par
/// item ; ramener l'organisation entière en un appel est plus rapide, et surtout déplace la
/// décision du côté testable de la frontière. C'est aussi ce qui permet de nommer l'ambiguïté
/// nous-mêmes, là où la CLI se contentait d'un « More than one result » qui ne dit pas quoi
/// renommer.
/// </para>
/// </remarks>
public sealed class SecretVault(IReadOnlyList<BwItem> items, string organisation)
{
    public SecretPresence Classify(SecretMarker marker)
    {
        var matches = Matches(marker);

        if (matches.Count == 0) return new(SecretPresenceKind.ItemMissing, null);
        if (matches.Count > 1) return new(SecretPresenceKind.Ambiguous, null);

        var value = SecretFieldResolver.Resolve(matches[0], marker.Field);

        return new(string.IsNullOrEmpty(value) ? SecretPresenceKind.FieldMissing : SecretPresenceKind.Found,
            matches[0].Id);
    }

    public SecretLookup Lookup(SecretMarker marker)
    {
        var matches = Matches(marker);

        if (matches.Count == 0)
            return SecretLookup.Missing(string.IsNullOrWhiteSpace(organisation)
                ? Loc.F("Inject_Error_ItemMissingVault", marker.Item)
                : Loc.F("Inject_Error_ItemMissingOrg", marker.Item, organisation));

        if (matches.Count > 1)
            return SecretLookup.Missing(Loc.F("Inject_Error_ItemAmbiguous", marker.Item));

        var value = SecretFieldResolver.Resolve(matches[0], marker.Field);

        return string.IsNullOrEmpty(value)
            ? SecretLookup.Missing(Loc.F("Inject_Error_EmptyField", marker.Item, marker.Field))
            : SecretLookup.Found(value);
    }

    private List<BwItem> Matches(SecretMarker marker) => items
        .Where(i => string.Equals(i.Name, marker.Item, StringComparison.OrdinalIgnoreCase))
        .ToList();
}
