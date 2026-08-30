namespace DockPad.Secrets;

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
    public SecretLookup Lookup(SecretMarker marker)
    {
        var matches = items
            .Where(i => string.Equals(i.Name, marker.Item, StringComparison.OrdinalIgnoreCase))
            .ToList();

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
}
