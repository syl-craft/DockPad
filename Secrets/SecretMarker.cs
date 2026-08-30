namespace DockPad.Secrets;

/// <summary>Un marqueur <c>{{ bw:item:champ }}</c>, réduit à ce qu'il désigne.</summary>
public readonly record struct SecretMarker(string Item, string Field)
{
    /// <summary>
    /// Forme courte affichée et journalisée : des <b>noms</b>, jamais la valeur qu'ils désignent.
    /// </summary>
    public override string ToString() => $"{Item}:{Field}";
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
