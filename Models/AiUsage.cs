namespace DockPad.Models;

/// <summary>
/// Instantané de consommation d'un fournisseur IA, tel qu'un <c>IUsageProvider</c> le rapporte.
/// </summary>
/// <remarks>
/// Propriétés <c>required</c>/<c>init</c> plutôt qu'un record positionnel : quatorze paramètres dont
/// plusieurs <c>string</c> consécutifs invitent à l'inversion silencieuse. L'objet n'est jamais
/// désérialisé — il est construit en code, à chaque lecture.
/// </remarks>
public sealed class AiUsage
{
    public required string ProviderId  { get; init; }
    public required string Name        { get; init; }
    public required string Glyph       { get; init; }
    public required string AccentColor { get; init; }

    public string Model { get; init; } = "";

    /// <summary>
    /// Coût déjà formaté, symbole de devise inclus (ex. <c>$3.80</c>). Chaîne et non décimal : le
    /// provider est le seul à savoir en quelle monnaie sa source facture, et DockPad ne convertit
    /// jamais. Vide = le fournisseur ne rapporte pas de coût.
    /// </summary>
    public string Cost { get; init; } = "";

    /// <summary>Jetons du bloc glissant de 5 h. 0 = notion absente chez ce fournisseur.</summary>
    public long SessionTokens { get; init; }
    /// <summary>Jetons du jour local en cours.</summary>
    public long DayTokens { get; init; }
    /// <summary>Jetons du mois local en cours.</summary>
    public long MonthTokens { get; init; }
    /// <summary>Appels du jour, après déduplication.</summary>
    public int Requests { get; init; }

    /// <summary>Quota de session. <c>null</c> = quota inconnu → la jauge se masque.</summary>
    public UsageWindow? Session { get; init; }
    /// <summary>Quota hebdomadaire. <c>null</c> = quota inconnu → la jauge se masque.</summary>
    public UsageWindow? Week { get; init; }

    /// <summary>Chiffres de démonstration : l'affichage doit le signaler.</summary>
    public bool IsDemo { get; init; }
}
