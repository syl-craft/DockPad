namespace DockPad.Models;

public class TerminalConfig
{
    public string ExePath           { get; set; } = "";
    public string StartingDirectory { get; set; } = "";
    public string RunCommand        { get; set; } = "";
    public bool   NewTab            { get; set; } = true;
    public string ExtraArgs         { get; set; } = "";

    /// <summary>
    /// Couleur d'onglet de la tuile (<c>#RGB</c> ou <c>#RRGGBB</c>) ; vide, celle tirée du nom du dossier.
    /// </summary>
    public string TabColor          { get; set; } = "";
}
