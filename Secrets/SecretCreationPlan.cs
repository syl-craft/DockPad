namespace DockPad.Secrets;

/// <summary>Un item que le formulaire propose : à créer, ou à compléter.</summary>
/// <param name="ItemId">L'item existant à compléter, ou <c>null</c> pour un item à créer.</param>
public sealed record SecretItemRequest(string ItemName, string? ItemId, IReadOnlyList<string> Fields)
{
    public bool IsNew => ItemId is null;
}

/// <summary>Une valeur saisie, pour un champ. Ne va jamais au journal.</summary>
public readonly record struct SecretFieldValue(string Field, string Value);

/// <summary>Ce qui sera réellement écrit : seulement les champs remplis.</summary>
public sealed record SecretItemCreation(string ItemName, string? ItemId, IReadOnlyList<SecretFieldValue> Fields);

/// <summary>Une collection d'organisation, où ranger un item créé.</summary>
public sealed record SecretCollection(string Id, string Name);

/// <summary>
/// PUR — ce que le fichier demande, ce que le coffre n'a pas, et ce qu'on écrira une fois le
/// formulaire rempli.
/// </summary>
/// <remarks>
/// Aucune de ces fonctions ne voit la CLI : elles reçoivent le tri du coffre en paramètre. C'est ce
/// qui permet de vérifier le groupement, le dédoublonnage et l'exclusion des ambigus sans coffre.
/// </remarks>
public static class SecretCreationPlan
{
    /// <summary>
    /// Les marqueurs que le fichier demande, dans l'ordre de première apparition, sans doublon.
    /// </summary>
    /// <remarks>
    /// Les trois sources — le texte en mode presse-papier, les annotations <c>x-bw</c> et les
    /// modèles <c>template:</c> en mode fichiers — sont fusionnées : un même champ ne doit donner
    /// qu'un seul champ de saisie, sinon on saisirait deux fois la même valeur.
    /// </remarks>
    public static IReadOnlyList<SecretMarker> Demanded(
        string content, SecretMode mode,
        IReadOnlyList<ComposeSecret> entries, IReadOnlyDictionary<string, string> templates)
    {
        var demanded = new List<SecretMarker>();

        if (mode is SecretMode.Clipboard or SecretMode.Both)
            demanded.AddRange(SecretTemplate.FindMarkers(content));

        foreach (var entry in entries)
        {
            if (entry.Marker is { } marker) demanded.Add(marker);
            else if (entry.Template is { } path && templates.TryGetValue(path, out var model))
                demanded.AddRange(SecretTemplate.FindMarkers(model));
        }

        return demanded.DistinctBy(Key).ToList();
    }

    /// <summary>Ce que le formulaire propose, groupé par item.</summary>
    /// <remarks>
    /// <see cref="SecretPresenceKind.Ambiguous"/> est exclu : écrire dans l'un des deux items au
    /// hasard serait pire que ne rien proposer, et l'erreur reste affichée comme avant.
    /// </remarks>
    public static IReadOnlyList<SecretItemRequest> Build(
        IEnumerable<SecretMarker> demanded, Func<SecretMarker, SecretPresence> classify)
    {
        var groups = new List<(string Name, string? Id, List<string> Fields)>();

        foreach (var marker in demanded.DistinctBy(Key))
        {
            var presence = classify(marker);
            if (presence.Kind is not (SecretPresenceKind.ItemMissing or SecretPresenceKind.FieldMissing)) continue;

            var index = groups.FindIndex(g => string.Equals(g.Name, marker.Item, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                groups.Add((marker.Item, presence.ItemId, []));
                index = groups.Count - 1;
            }

            groups[index].Fields.Add(marker.Field);
        }

        return groups.Select(g => new SecretItemRequest(g.Name, g.Id, g.Fields)).ToList();
    }

    /// <summary>
    /// Les valeurs saisies. Un champ vide n'est pas créé ; un item sans aucun champ rempli disparaît.
    /// </summary>
    /// <remarks>
    /// Pour la même raison qu'un fichier de secret n'est jamais écrit vide : un champ vide dans le
    /// coffre ferait croire que le secret existe.
    /// </remarks>
    public static IReadOnlyList<SecretItemCreation> WithValues(
        IReadOnlyList<SecretItemRequest> requests, Func<string, string, string?> valueOf) =>
        requests
            .Select(r => new SecretItemCreation(r.ItemName, r.ItemId,
                r.Fields.Select(f => new SecretFieldValue(f, valueOf(r.ItemName, f) ?? ""))
                        .Where(v => v.Value.Length > 0)
                        .ToList()))
            .Where(c => c.Fields.Count > 0)
            .ToList();

    /// <summary>
    /// La collection proposée d'office : celle du réglage, par nom ou par identifiant, sinon la
    /// première par ordre alphabétique.
    /// </summary>
    /// <returns>
    /// <c>ConfiguredMissing</c> vaut vrai quand un nom était réglé et n'existe pas : l'écran le
    /// signale, sinon on rangerait l'item ailleurs sans que personne le sache.
    /// </returns>
    public static (SecretCollection? Selected, bool ConfiguredMissing) DefaultCollection(
        IReadOnlyList<SecretCollection> collections, string configured)
    {
        var match = string.IsNullOrWhiteSpace(configured) ? null : collections.FirstOrDefault(c =>
            string.Equals(c.Name, configured.Trim(), StringComparison.OrdinalIgnoreCase)
         || string.Equals(c.Id, configured.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match is not null) return (match, false);

        var first = collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault();

        return (first, !string.IsNullOrWhiteSpace(configured));
    }

    private static (string, string) Key(SecretMarker m) =>
        (m.Item.ToUpperInvariant(), m.Field.ToUpperInvariant());
}
