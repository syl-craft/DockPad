namespace DockPad.Models;

/// <summary>
/// Un terminal déclaré dans la section <c>terminals</c> de <c>settings.json</c>, proposé dans la
/// liste « Terminal par défaut » des Options quand son programme existe.
/// </summary>
public class TerminalDefinition : TerminalSettings
{
    /// <summary>
    /// Identifiant enregistré dans <c>defaultTerminal</c> ; ni vide, ni <c>wt</c>, <c>pwsh</c>,
    /// <c>powershell</c> ou <c>custom</c>, réservés.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Nom affiché dans la liste ; vide, l'identifiant.
    /// </summary>
    public string Name { get; set; } = "";
}
