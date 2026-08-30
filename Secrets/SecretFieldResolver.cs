namespace DockPad.Secrets;

/// <summary>
/// L'ordre dans lequel un marqueur cherche son champ : le personnalisé d'abord, les standards
/// ensuite.
/// </summary>
/// <remarks>
/// Un champ personnalisé <b>gagne toujours</b> sur le champ standard du même nom, et ne retombe pas
/// dessus s'il est vide : le champ qu'on a nommé soi-même existe, et le dire franchement vaut mieux
/// que d'aller chercher ailleurs une valeur que personne n'a demandée. C'est l'ordre du script
/// d'origine, conservé tel quel.
/// </remarks>
public static class SecretFieldResolver
{
    public static string? Resolve(BwItem item, string field)
    {
        var custom = item.Fields?.FirstOrDefault(
            f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase));
        if (custom is not null) return custom.Value;

        return field.ToLowerInvariant() switch
        {
            "password" => item.Login?.Password,
            "username" => item.Login?.Username,
            "notes" => item.Notes,
            "totp" => item.Login?.Totp,
            _ => null,
        };
    }
}
