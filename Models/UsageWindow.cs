namespace DockPad.Models;

/// <summary>Une fenêtre de quota : ce qui est consommé, et quand ça repart à zéro.</summary>
public sealed class UsageWindow
{
    /// <summary>Pourcentage consommé, borné 0-100.</summary>
    public required int UsedPct { get; init; }

    /// <summary>
    /// Heure locale de remise à zéro, ou <c>null</c> si la source ne la donne pas. Date et non
    /// chaîne : « 14h00 » ou « lun. 00h » est une décision d'affichage, et une date se teste.
    /// </summary>
    public DateTime? ResetsAt { get; init; }

    /// <summary>Pourcentage restant, ce que l'utilisateur lit réellement sur la jauge.</summary>
    public int RemainingPct => 100 - UsedPct;
}
