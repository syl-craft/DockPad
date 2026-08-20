using System.Globalization;

namespace DockPad.Services.Usage;

/// <summary>
/// Mise en forme du bandeau Usage IA. Fonctions pures : aucune horloge implicite, aucune culture
/// implicite — les deux sont passées en paramètre ou figées, pour que le rendu soit testable et
/// identique sur toutes les machines.
/// </summary>
public static class UsageFormat
{
    /// <summary>Restant au niveau du seuil configuré ou en dessous.</summary>
    public const string Critical = "#E5484D";
    /// <summary>Consommation élevée, sans être critique.</summary>
    public const string High = "#F5A623";
    /// <summary>Consommation confortable.</summary>
    public const string Ok = "#34A853";

    /// <summary>
    /// La culture d'affichage est figée : DockPad est une application française, et le rendu ne doit
    /// pas changer selon les paramètres régionaux de la machine (« 12,4k » ici, « 12.4k » ailleurs).
    /// </summary>
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Couleur d'une jauge. Le seuil porte sur le <b>restant</b> — c'est ce que l'utilisateur règle
    /// (« alerte-moi quand il me reste moins de N % ») — et prime sur la bascule ambre à 60 % de
    /// consommé : un seuil à 50 % rend donc tout rouge dès la moitié.
    /// </summary>
    public static string GaugeColor(int usedPct, int thresholdPct)
    {
        if (100 - usedPct <= thresholdPct) return Critical;
        if (usedPct >= 60) return High;
        return Ok;
    }

    /// <summary>987 → « 987 », 12 400 → « 12,4k », 1 200 000 → « 1,2M ».</summary>
    public static string Tokens(long n)
    {
        if (n < 0) return "—";
        if (n < 1_000) return n.ToString(Fr);
        if (n < 1_000_000) return Trim(n / 1_000d) + "k";
        return Trim(n / 1_000_000d) + "M";
    }

    /// <summary>
    /// Heure de remise à zéro d'un quota : « 14h00 » si c'est aujourd'hui, « lun. 00h » sinon,
    /// chaîne vide si la source ne la donne pas. <paramref name="now"/> est explicite : sans lui la
    /// fonction dépendrait de l'horloge et ne serait pas testable.
    /// </summary>
    public static string Reset(DateTime? resetsAt, DateTime now)
    {
        if (resetsAt is not { } reset) return "";
        return reset.Date == now.Date
            ? reset.ToString(@"HH\hmm", Fr)
            : reset.ToString(@"ddd HH\h", Fr);
    }

    /// <summary>Une décimale, mais pas de « ,0 » inutile : 1 000 → « 1 », 1 050 → « 1,1 ».</summary>
    private static string Trim(double value)
    {
        var s = value.ToString("0.#", Fr);
        return s;
    }
}
