using System.ComponentModel;

namespace DockPad.Services.Usage;

/// <summary>
/// Ce que la barre peut montrer : logos et style complets, charge du PC seule, ou une notice.
/// </summary>
public enum BarState { Full, LoadOnly, Empty }

/// <summary>Identité stable d'une jauge, indépendante du libellé et des relevés.</summary>
public enum BarGaugeKind { Session, Week, Cpu, Memory }
public sealed record BarGauge(string ProviderId, BarGaugeKind Kind);

/// <summary>La jauge en grand et l'identité affichée au-dessus ; la charge n'a pas de page fournisseur.</summary>
public sealed record BarFeaturedGauge(BarRing Ring, string Glyph, string Name, string WindowLabel, string UsageUrl)
{
    public bool CanOpenUsagePage => Ring.ProviderId.Length > 0 && UsagePageLink.IsWebUrl(UsageUrl, out _);
}

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
    public UsageHoverCard? HoverCard { get; init; }
    public string NumberText { get; init; } = "";
    public string NumberBrushKey { get; init; } = "";
}

/// <summary>
/// Un anneau des styles Anneaux et Réservoir. IsEmpty : l'emplacement existe mais la fenêtre manque. NumberText et
/// NumberBrushKey : voir <see cref="BarColumn"/>.
/// </summary>
public sealed record BarRing(string Label, string Text, double Fraction, string Color, string Tooltip,
    string ProviderId = "", bool IsEmpty = false, bool IsWarning = false) : INotifyPropertyChanged
{
    public UsageHoverCard? HoverCard { get; init; }
    public string Text { get; private set; } = Text;
    public double Fraction { get; private set; } = Fraction;
    public string Tooltip { get; private set; } = Tooltip;
    private string _numberText = "";
    public string NumberText { get => _numberText; init => _numberText = value; }
    public string NumberBrushKey { get; init; } = "";
    public BarGauge? Selection { get; init; }
    public bool CanEnlarge => Selection != null && IsEmpty == false && IsWarning == false;
    public string EnlargeTooltip => Loc.F("UsageBar_Enlarge_Tooltip", Tooltip);

    public event PropertyChangedEventHandler? PropertyChanged;

    // Les abonnements WPF ne font pas partie de la valeur : un relevé identique conserve les éléments liés.
    public bool Equals(BarRing? other) => other is { }
        && Label == other.Label && Text == other.Text && Fraction == other.Fraction && Color == other.Color
        && Tooltip == other.Tooltip && ProviderId == other.ProviderId && IsEmpty == other.IsEmpty
        && IsWarning == other.IsWarning && NumberText == other.NumberText
        && NumberBrushKey == other.NumberBrushKey && Selection == other.Selection && HoverCard == other.HoverCard;

    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(Label, Text, Fraction, Color, Tooltip, ProviderId, IsEmpty, IsWarning),
        NumberText, NumberBrushKey, Selection, HoverCard);

    /// <summary>Une mesure ne remplace pas l'anneau lié à un bouton, son focus et son infobulle.</summary>
    internal void UpdateLoad(string text, double fraction, string tooltip, string numberText)
    {
        if (Text == text && Fraction == fraction && Tooltip == tooltip && NumberText == numberText) return;

        Text = text;
        Fraction = fraction;
        Tooltip = tooltip;
        _numberText = numberText;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

/// <summary>
/// Une ligne du style Héros : une fenêtre de l'IA mise en avant, ou la fenêtre la plus consommée d'un autre fournisseur.
/// </summary>
public sealed record BarHeroRow(string ProviderId, string Glyph, string Label, string Text, double Fraction,
    string Color, string Tooltip, bool IsWarning = false)
{
    public UsageHoverCard? HoverCard { get; init; }
}

/// <summary>
/// La carte de l'IA mise en avant. En panne de quota : IsWarning, NoticeTitle, et aucun chiffre.
/// </summary>
public sealed record BarHeroCard(string ProviderId, string Glyph, string Name, string Label, string Text,
    double Fraction, string Color, string ResetText, bool IsWarning, string NoticeTitle, string Tooltip,
    BarHeroRow? SecondWindow)
{
    public UsageHoverCard? HoverCard { get; init; }
    /// <summary>« ↻ 19h30 », ou vide quand la remise à zéro est inconnue.</summary>
    public string ResetLabel { get; init; } = "";
}

/// <summary>
/// Anneaux de l'IA mise en avant, puis les autres en petits anneaux. Après un choix manuel, Outer porte seulement
/// la jauge choisie (quota ou charge) et Inner est null.
/// </summary>
public sealed record BarRings(BarRing? Outer, BarRing? Inner, string CenterText, IReadOnlyList<BarRing> Others)
{
    /// <summary>« 5h / 7j » : les noms courts des deux anneaux, ou celui de l'anneau simple.</summary>
    public string Legend { get; init; } = "";

    /// <summary>Le texte complet de la légende : l'infobulle de chacun des deux anneaux, l'une sous l'autre.</summary>
    public string LegendTooltip { get; init; } = "";
}

/// <summary>
/// Niveau de la jauge choisie, sinon de la fenêtre la plus consommée, et petites pastilles.
/// Label porte le titre court de la notice quand l'IA mise en avant est en panne de quota.
/// </summary>
public sealed record BarReservoir(bool HasLevel, double Level, string LevelText, string Label, string Color,
    string Tooltip,
    IReadOnlyList<BarRing> Dots)
{
    public UsageHoverCard? HoverCard { get; init; }
}

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
