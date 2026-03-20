namespace DockPad.Models;

public class ProcessSwitchConfig
{
    /// <summary>Nom du processus à rechercher (ex: devenv.exe).</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Chemin complet vers l'exécutable à lancer si le processus n'est pas trouvé.</summary>
    public string Executable  { get; set; } = "";

    /// <summary>Paramètres passés au lancement et recherchés dans la ligne de commande existante.</summary>
    public string Parameters  { get; set; } = "";
}
