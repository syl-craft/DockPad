using System.Text.RegularExpressions;

namespace DockPad.Services;

/// <summary>
/// Agent IA local lancé par Ollama : <c>ollama launch &lt;harness&gt; --model &lt;modèle&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation par liste blanche, jamais par échappement.</b> Les deux réglages finissent dans
/// une commande du registre, que DockPad transmet ensuite au terminal par défaut — à PowerShell
/// <c>-Command</c> pour certains. Échapper correctement pour toutes ces couches à la fois est hors
/// de portée ; n'accepter que des caractères qui ne séparent, ne citent ni ne développent rien
/// règle le problème une fois pour toutes. Les noms réels tiennent dans ces listes : les
/// intégrations d'Ollama (<c>claude</c>, <c>copilot-cli</c>…) et ses modèles
/// (<c>ministral-3:3b</c>, <c>hf.co/org/modele:Q4_K_M</c>).
/// </para>
/// <para>
/// <b>Le premier caractère n'est jamais un tiret</b> : <c>--restore</c> ou <c>--help</c> seraient
/// lus par Ollama comme une option, pas comme un nom.
/// </para>
/// </remarks>
public static class OllamaAgent
{
    // \z et non $ : en .NET, $ accepte encore un saut de ligne final.
    private static readonly Regex HarnessPattern = new(@"^[a-z0-9][a-z0-9-]{0,39}\z");
    private static readonly Regex ModelPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,99}\z");

    /// <summary>Caractères autorisés, affichés quand une saisie est refusée.</summary>
    public const string HarnessAllowed = "a-z 0-9 -";
    public const string ModelAllowed = "A-Z a-z 0-9 . _ : / -";

    public static bool IsValidHarness(string harness) => HarnessPattern.IsMatch(harness);

    public static bool IsValidModel(string model) => ModelPattern.IsMatch(model);

    /// <summary>
    /// La commande lancée dans le dossier du clic droit, ou <c>null</c> si le réglage est
    /// incomplet ou refusé.
    /// </summary>
    public static string? RunCommand(string ollamaExe, string harness, string model)
    {
        if (!IsValidHarness(harness) || !IsValidModel(model)) return null;

        return $"\"{ollamaExe}\" launch {harness} --model {model}";
    }
}
