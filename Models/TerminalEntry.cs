using System.Text.Json.Serialization;

namespace DockPad.Models;

/// <summary>
/// Un terminal de la fenêtre Terminaux : détecté ou ajouté, avec ses deux modèles d'arguments.
/// </summary>
public class TerminalEntry
{
    /// <summary>
    /// Identifiant enregistré dans <c>defaultTerminal</c> : celui de la détection (<c>wt</c>,
    /// <c>wezterm</c>…) pour un terminal détecté, tiré du nom pour un terminal ajouté.
    /// </summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>
    /// Nom donné par la détection : tant que <see cref="Name"/> lui est égal, il suit la détection.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DetectedName { get; set; }

    /// <summary>
    /// Programme ; les variables d'environnement (<c>%LocalAppData%</c>) sont admises.
    /// </summary>
    public string ExePath { get; set; } = "";
    public string FolderArgs { get; set; } = "";
    public string CommandArgs { get; set; } = "";

    /// <summary>
    /// Masqué = absent de la liste « Terminal par défaut » des Options, conservé ici.
    /// </summary>
    public bool Hidden { get; set; }

    public int Order { get; set; }
}
