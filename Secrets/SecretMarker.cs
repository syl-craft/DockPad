namespace DockPad.Secrets;

/// <summary>Un marqueur <c>{{ bw:item:champ }}</c>, réduit à ce qu'il désigne.</summary>
public readonly record struct SecretMarker(string Item, string Field)
{
    /// <summary>
    /// Forme courte affichée et journalisée : des <b>noms</b>, jamais la valeur qu'ils désignent.
    /// </summary>
    public override string ToString() => $"{Item}:{Field}";

    /// <summary>
    /// Ce que désigne <see cref="Field"/> : un champ ou une pièce jointe, et une propriété JSON à extraire.
    /// </summary>
    public SecretFieldReference Reference => SecretFieldReference.Parse(Field);
}

/// <summary>
/// Le champ d'un marqueur, découpé : <c>&lt;source&gt;[|json:&lt;chemin&gt;]</c>, où la source est un
/// nom de champ ou <c>@&lt;nom de pièce jointe&gt;</c>.
/// </summary>
/// <param name="HasUnknownFilter">
/// Un suffixe <c>|…</c> autre qu'un <c>|json:</c> suivi d'un chemin : le marqueur échoue plutôt que
/// de rendre la valeur brute.
/// </param>
public sealed record SecretFieldReference(string Name, bool IsAttachment, string? JsonPath, bool HasUnknownFilter)
{
    private const string AttachmentPrefix = "@";
    private const string JsonFilter = "|json:";

    /// <summary>
    /// Un champ lu tel quel : le seul cas que le formulaire de création sait écrire.
    /// </summary>
    public bool IsPlain => !IsAttachment && JsonPath == null && !HasUnknownFilter;

    public static SecretFieldReference Parse(string field)
    {
        var pipe = field.IndexOf('|');
        var source = pipe < 0 ? field : field[..pipe];
        var filter = pipe < 0 ? null : field[pipe..];

        var isAttachment = source.StartsWith(AttachmentPrefix, StringComparison.Ordinal);
        var name = isAttachment ? source[AttachmentPrefix.Length..] : source;

        if (filter == null) return new(name, isAttachment, null, false);

        var isJson = filter.StartsWith(JsonFilter, StringComparison.Ordinal) && filter.Length > JsonFilter.Length;

        return isJson
            ? new(name, isAttachment, filter[JsonFilter.Length..], false)
            : new(name, isAttachment, null, true);
    }
}

/// <summary>
/// Ce que le coffre répond pour un marqueur : une valeur, ou la raison de son absence.
/// </summary>
/// <remarks>
/// Le motif d'échec voyage avec la réponse plutôt que d'être reconstruit plus haut : seul celui qui
/// a cherché sait si l'item manquait, s'il y en avait deux, ou si c'est le champ qui était vide —
/// et ces trois cas appellent trois corrections différentes.
/// </remarks>
public readonly record struct SecretLookup(string? Value, string? Failure)
{
    public static SecretLookup Found(string value) => new(value, null);

    public static SecretLookup Missing(string failure) => new(null, failure);
}
