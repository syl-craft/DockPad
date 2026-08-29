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
    // La culture d'affichage, pas une culture fixe : « 12,4k » en français, « 12.4k » en anglais.
    private static CultureInfo Display => Loc.Formatting;

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

    /// <summary>
    /// 987 → « 987 », 12 400 → « 12,4k », 1 200 000 → « 1,2M », 2 740 000 000 → « 2,7 Md ».
    /// </summary>
    /// <remarks>
    /// Le palier « Md » n'est pas décoratif : un mois d'usage soutenu de Claude Code dépasse le
    /// milliard de jetons (mesuré : 2,74 Md sur un mois), et sans lui la colonne afficherait
    /// « 2741,9M ».
    /// </remarks>
    public static string Tokens(long n)
    {
        if (n < 0) return "—";
        if (n < 1_000) return n.ToString(Display);

        // Les seuils portent sur la valeur APRÈS arrondi, pas avant : 999 999 divisé par mille donne
        // 999,999, qui s'arrondit à une décimale en 1000 — donc « 1000k » au lieu de « 1M ». La
        // frontière est là où l'arrondi reste sous le millier.
        // Les suffixes viennent des ressources : « Md » est francais, l'anglais dit « B », et
        // l'espace qui precede l'un mais pas l'autre fait partie du choix typographique de la langue.
        if (n < 999_950) return Trim(n / 1_000d) + Loc.T("Usage_Suffix_Thousand");
        if (n < 999_950_000) return Trim(n / 1_000_000d) + Loc.T("Usage_Suffix_Million");
        return Trim(n / 1_000_000_000d) + Loc.T("Usage_Suffix_Billion");
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
            // Le gabarit lui-meme est traduit : le « h » separateur de « 11h54 » est une convention
            // francaise, qu'aucune CultureInfo ne corrige puisqu'il est ecrit en dur.
            ? reset.ToString(Loc.T("Usage_TimeFormat"), Display)
            : reset.ToString(Loc.T("Usage_DayTimeFormat"), Display);
    }

    /// <summary>Une décimale, mais pas de « ,0 » inutile : 1 000 → « 1 », 1 050 → « 1,1 ».</summary>
    private static string Trim(double value)
    {
        var s = value.ToString("0.#", Display);
        return s;
    }
}
