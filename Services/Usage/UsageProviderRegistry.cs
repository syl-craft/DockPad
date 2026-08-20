namespace DockPad.Services.Usage;

/// <summary>
/// Seul point d'enregistrement des fournisseurs. Ajouter Codex, Gemini ou Copilot, c'est une classe
/// et une ligne ici — l'interface, la config, l'UI et les tests ne bougent pas, parce que l'identifiant
/// du fournisseur ne change pas.
/// </summary>
public static class UsageProviderRegistry
{
    /// <summary>
    /// Les fournisseurs de production. <c>UsageService</c> accepte une liste en paramètre : les tests
    /// et l'outil de capture substituent la leur, ce registre reste la vérité de l'application.
    /// </summary>
    public static IReadOnlyList<IUsageProvider> All { get; } =
    [
        new ClaudeUsageProvider(),
        DemoUsageProvider.Default(),
    ];
}
