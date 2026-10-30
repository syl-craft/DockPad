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
/// Une barre du style Colonnes. Fraction va de 0 à 1 ; Glyph est vide pour le processeur et la mémoire.
/// </summary>
public sealed record BarColumn(string Label, string Text, double Fraction, string Color, string Tooltip,
    string Glyph = "", bool IsWarning = false);

/// <summary>
/// Un anneau des styles Anneaux et Réservoir. IsEmpty : l'emplacement existe mais la fenêtre manque.
/// </summary>
public sealed record BarRing(string Label, string Text, double Fraction, string Color, string Tooltip,
    string ProviderId = "", bool IsEmpty = false, bool IsWarning = false);

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
    BarHeroRow? SecondWindow);

/// <summary>
/// Anneau extérieur (Session) et intérieur (Week) de l'IA mise en avant, puis les autres en petits anneaux.
/// Inner est null quand il n'y a qu'une fenêtre.
/// </summary>
public sealed record BarRings(BarRing? Outer, BarRing? Inner, string CenterText, IReadOnlyList<BarRing> Others);

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
    string CpuColor, string MemoryColor, string Tooltip, IReadOnlyList<int?> CpuSeries, IReadOnlyList<int?> MemorySeries);
