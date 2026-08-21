namespace DockPad.Services.Usage;

/// <summary>
/// Seul point d'enregistrement des fournisseurs. Ajouter un assistant, c'est une classe et une ligne
/// ici — l'interface, la config, l'UI et les tests ne bougent pas, parce que l'identifiant du
/// fournisseur ne change pas. Codex, Gemini et Copilot l'ont vérifié : leur arrivée n'a touché ni le
/// bandeau ni la fenêtre de réglages.
/// </summary>
public static class UsageProviderRegistry
{
    /// <summary>
    /// Les fournisseurs de production. <c>UsageService</c> accepte une liste en paramètre : les tests
    /// et l'outil de capture substituent la leur, ce registre reste la vérité de l'application.
    ///
    /// Seul Claude rapporte un quota et un coût. Les trois autres n'exposent pas de pourcentage de
    /// limite lisible localement, et je n'ai pas de tarif public fiable à leur appliquer : leurs
    /// jauges restent masquées et leur colonne de coût affiche un tiret.
    /// </summary>
    public static IReadOnlyList<IUsageProvider> All { get; } =
    [
        new ClaudeUsageProvider(),
        new CodexUsageProvider(),
        new GeminiUsageProvider(),
        new CopilotUsageProvider(),
        DemoUsageProvider.Default(),
    ];
}
