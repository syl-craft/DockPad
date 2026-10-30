namespace DockPad.Services.Usage;

/// <summary>
/// Ce que la barre peut montrer : logos et style complets, charge du PC seule, ou une notice.
/// </summary>
public enum BarState { Full, LoadOnly, Empty }

/// <summary>
/// Un logo de la tête de barre. Le point d'alerte n'est allumé que sur un fournisseur non mis en avant.
/// </summary>
public sealed record BarLogo(string ProviderId, string Glyph, string Color, bool IsSelected, bool HasAlertDot,
    string Tooltip, string AccessibleName, string UsageUrl);

/// <summary>
/// Une barre du style Colonnes. Fraction va de 0 à 1 ; Glyph et ProviderId sont vides pour le processeur et la mémoire.
/// NumberText est le nombre seul, sans signe de pourcentage ; NumberBrushKey nomme la brosse du thème qui le colore,
/// vide pour garder Color.
/// </summary>
public sealed record BarColumn(string Label, string Text, double Fraction, string Color, string Tooltip,
    string Glyph = "", bool IsWarning = false, string ProviderId = "")
{
    public string NumberText { get; init; } = "";
    public string NumberBrushKey { get; init; } = "";
}

/// <summary>
/// Un anneau des styles Anneaux et Réservoir. IsEmpty : l'emplacement existe mais la fenêtre manque. NumberText et
/// NumberBrushKey : voir <see cref="BarColumn"/>.
/// </summary>
public sealed record BarRing(string Label, string Text, double Fraction, string Color, string Tooltip,
    string ProviderId = "", bool IsEmpty = false, bool IsWarning = false)
{
    public string NumberText { get; init; } = "";
    public string NumberBrushKey { get; init; } = "";
}

/// <summary>
/// Une ligne du style Héros : une fenêtre de l'IA mise en avant, ou la fenêtre la plus consommée d'un autre fournisseur.
/// </summary>
public sealed record BarHeroRow(string ProviderId, string Glyph, string Label, string Text, double Fraction,
    string Color, string Tooltip, bool IsWarning = false);

/// <summary>
/// La carte de l'IA mise en avant. En panne de quota : IsWarning, NoticeTitle, et aucun chiffre.
/// </summary>
public sealed record BarHeroCard(string ProviderId, string Glyph, string Name, string Label, string Text,
    double Fraction, string Color, string ResetText, bool IsWarning, string NoticeTitle, string Tooltip,
    BarHeroRow? SecondWindow)
{
    /// <summary>« ↻ 19h30 », ou vide quand la remise à zéro est inconnue.</summary>
    public string ResetLabel { get; init; } = "";
}

/// <summary>
/// Anneau extérieur (Session) et intérieur (Week) de l'IA mise en avant, puis les autres en petits anneaux.
/// Inner est null quand il n'y a qu'une fenêtre.
/// </summary>
public sealed record BarRings(BarRing? Outer, BarRing? Inner, string CenterText, IReadOnlyList<BarRing> Others)
{
    /// <summary>« 5h / 7j » : les noms courts des deux anneaux, ou celui de l'anneau simple.</summary>
    public string Legend { get; init; } = "";
}

/// <summary>
/// Niveau de la fenêtre la plus consommée et quatre pastilles ; HasLevel est faux sans IA mise en avant.
/// Label porte le titre court de la notice quand l'IA mise en avant est en panne de quota.
/// </summary>
public sealed record BarReservoir(bool HasLevel, double Level, string LevelText, string Label, string Color,
    string Tooltip,
    IReadOnlyList<BarRing> Dots);

/// <summary>
/// Charge du PC. Un processeur inconnu s'écrit « — » et laisse un trou (null) dans sa série.
/// </summary>
public sealed record BarLoad(string CpuText, string MemoryText, double CpuFraction, double MemoryFraction,
    string CpuColor, string MemoryColor, string Tooltip, IReadOnlyList<int?> CpuSeries, IReadOnlyList<int?> MemorySeries)
{
    /// <summary>« CPU / RAM », sous le double anneau du processeur et de la mémoire.</summary>
    public string Legend { get; init; } = "";

    /// <summary>Les nombres seuls, sans signe de pourcentage.</summary>
    public string CpuNumber { get; init; } = "";
    public string MemoryNumber { get; init; } = "";
}
