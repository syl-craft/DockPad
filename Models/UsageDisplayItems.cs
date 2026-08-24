using System.ComponentModel;

namespace DockPad.Models;

/// <summary>Un onglet de fournisseur dans le bandeau.</summary>
public sealed class UsageTabItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string ProviderId { get; init; }
    public required string Name { get; init; }
    public required string Glyph { get; init; }
    public required string Accent { get; init; }
    public bool IsDemo { get; init; }

    private bool _isLoading;

    /// <summary>Une lecture est en cours pour ce fournisseur : la pastille cède la place au sablier.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            Notify(nameof(IsLoading));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            // Les trois propriétés dérivées changent avec la sélection : sans ces notifications,
            // l'onglet actif garde l'apparence de l'onglet inactif.
            Notify(nameof(IsSelected));
            Notify(nameof(Background));
            Notify(nameof(BorderBrush));
            Notify(nameof(Weight));
        }
    }

    public string Background => IsSelected ? "#F3F3F3" : "#FFFFFF";
    public string BorderBrush => IsSelected ? "#C8C8C8" : "#E6E6E6";
    public string Weight => IsSelected ? "SemiBold" : "Normal";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Une colonne de métrique sous les jauges.</summary>
public sealed class UsageMetric
{
    public required string Label { get; init; }
    public required string Value { get; init; }

    /// <summary>Précision au survol. Vide = pas d'infobulle.</summary>
    public string Tooltip { get; init; } = "";
}

/// <summary>Une jauge de quota.</summary>
public sealed class UsageGaugeItem
{
    public required string Label { get; init; }

    /// <summary>
    /// Pourcentage <b>consommé</b> : le nombre affiché et la largeur de la barre. C'est ce que
    /// compte la page claude.ai/settings/usage, et il ne faut pas deux références qui se
    /// contredisent pour la même donnée.
    /// </summary>
    public int UsedPct { get; init; }

    /// <summary>Pourcentage restant. Conservé pour le calcul du seuil d'alerte, non affiché.</summary>
    public int RemainingPct { get; init; }

    public string Reset { get; init; } = "";

    /// <summary>Infobulle de l'heure de remise à zéro, construite par le ViewModel.</summary>
    public string ResetTooltip { get; init; } = "";
    public string Color { get; init; } = "";

    /// <summary>Précision au survol : le libellé affiché est volontairement court.</summary>
    public string Tooltip { get; init; } = "";

    /// <summary>
    /// Le fournisseur expose-t-il un quota ? Faux → la barre est remplacée par « quota inconnu »
    /// plutôt que par une barre vide, qui se lirait comme « rien consommé ».
    /// </summary>
    public bool HasQuota { get; init; }
}
