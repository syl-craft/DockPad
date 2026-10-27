using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Un abonné défaillant à <c>McpDispatcher.Mutated</c> est isolé : les abonnés suivants sont appelés et
/// rien ne remonte vers le traitement de la requête. Le test ne passe par aucun outil d'écriture, pour ne
/// jamais toucher au profil ni figer les chemins statiques des services sur un dossier temporaire.
/// </summary>
public class McpDispatcherMutationTests
{
    [Fact]
    public void Mutated_ExceptionDUnAbonne_AutresAppelesSansRemontee()
    {
        int calls = 0;
        Action failing = () => throw new InvalidOperationException("Subscriber failed.");
        Action following = () => calls++;
        McpDispatcher.Mutated += failing;
        McpDispatcher.Mutated += following;
        try
        {
            var exception = Record.Exception(McpDispatcher.RaiseMutated);

            Assert.Null(exception);
            Assert.Equal(1, calls);
        }
        finally
        {
            McpDispatcher.Mutated -= failing;
            McpDispatcher.Mutated -= following;
        }
    }
}
