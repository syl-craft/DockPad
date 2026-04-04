namespace DockPad.Models;

public enum ProcessSearchMode { ByProcessName, ByWindowTitle }

public class ProcessSwitchConfig
{
    /// <summary>Mode de recherche : par nom de processus ou par titre de fenêtre.</summary>
    public ProcessSearchMode SearchMode { get; set; } = ProcessSearchMode.ByProcessName;

    /// <summary>Nom du processus (ex: devenv.exe) ou titre de fenêtre selon SearchMode.</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Chemin complet vers l'exécutable à lancer si le processus n'est pas trouvé.</summary>
    public string Executable  { get; set; } = "";

    /// <summary>Paramètres passés au lancement et recherchés dans la ligne de commande existante (ByProcessName uniquement).</summary>
    public string Parameters  { get; set; } = "";
}
