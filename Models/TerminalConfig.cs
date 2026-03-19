namespace DockPad.Models;

public class TerminalConfig
{
    public string ExePath           { get; set; } = "";
    public string StartingDirectory { get; set; } = "";
    public string RunCommand        { get; set; } = "";
    public bool   NewTab            { get; set; } = true;
    public string ExtraArgs         { get; set; } = "";
}
