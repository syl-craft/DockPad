namespace DockPad.Services.Usage;

/// <summary>Bornes de scan, communes aux fournisseurs qui lisent des fichiers.</summary>
public static class UsageWindows
{
    /// <summary>
    /// Borne basse d'un scan : le début du mois, sauf le premier du mois au petit matin où le bloc
    /// de session actif peut avoir démarré le mois précédent.
    /// </summary>
    /// <remarks>
    /// Cette borne sert au filtre par date de modification : un fichier plus ancien ne peut pas
    /// contenir d'entrée dans la fenêtre, et ne sera donc pas ouvert.
    /// </remarks>
    public static DateTime ScanStart(DateTime now)
    {
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var blockStart = now - UsageAggregator.BlockWindow;
        return monthStart < blockStart ? monthStart : blockStart;
    }
}
