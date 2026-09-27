using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Secrets;

/// <summary>Ce qui empêche d'aller plus loin : une phrase pour l'écran, un diagnostic pour le journal.</summary>
/// <param name="Diagnostic">
/// Technique, et <b>jamais traduit</b> — il va au journal et en infobulle derrière une phrase, elle,
/// traduite. Même règle que les causes d'indisponibilité du quota dans le bandeau Usage. Il ne porte
/// jamais de matière secrète : ni le mot de passe, ni la sortie standard de la CLI.
/// </param>
public sealed record SecretSourceFailure(string Message, string? Diagnostic = null);

/// <summary>
/// Un coffre ouvert : de quoi résoudre un marqueur, ou la raison d'un refus.
/// </summary>
/// <param name="Lookup">De quoi résoudre un marqueur.</param>
/// <param name="Warning">Ce qui n'empêche pas de travailler mais ne doit pas se taire.</param>
/// <param name="Classify">Le tri d'un marqueur — trouvé, item absent, champ absent, ambigu.</param>
/// <param name="Writer">
/// De quoi écrire ce qui manque. <c>null</c> pour une source en lecture seule : le formulaire de
/// création n'apparaît alors jamais.
/// </param>
public sealed record SecretSourceOpening(
    Func<SecretMarker, SecretLookup>? Lookup,
    SecretSourceFailure? Failure = null,
    string? Warning = null,
    Func<SecretMarker, SecretPresence>? Classify = null,
    SecretWriter? Writer = null);

/// <summary>Le coffre relu après écriture, et ce qui a été refusé.</summary>
public sealed record SecretWriteOutcome(
    Func<SecretMarker, SecretLookup> Lookup,
    Func<SecretMarker, SecretPresence> Classify,
    IReadOnlyList<string> Failures);

/// <summary>
/// Ce qui sait écrire dans le coffre ouvert — et qui tient la clé de session le temps d'une injection.
/// </summary>
/// <remarks>
/// <para>
/// <b>La clé n'est pas un champ.</b> Elle vit dans la fermeture <c>write</c> fabriquée par la
/// source ; ni la fenêtre ni le service ne la voient jamais. <see cref="Forget"/> lâche la
/// fermeture : la clé devient inatteignable, et une écriture tardive lève au lieu d'écrire.
/// </para>
/// <para>
/// C'est le seul assouplissement de « aucune clé conservée » : le formulaire s'intercale entre la
/// lecture et l'écriture, donc la clé doit survivre à la saisie — et à rien de plus.
/// </para>
/// </remarks>
public sealed class SecretWriter(
    string? organisation,
    IReadOnlyList<SecretCollection> collections,
    Func<IReadOnlyList<SecretItemCreation>, string?, CancellationToken, Task<SecretWriteOutcome>> write)
{
    private Func<IReadOnlyList<SecretItemCreation>, string?, CancellationToken, Task<SecretWriteOutcome>>? _write = write;

    /// <summary>L'organisation où les items seront créés, ou <c>null</c> pour le coffre personnel.</summary>
    public string? Organisation { get; } = organisation;

    /// <summary>Les collections de l'organisation. Vide sans organisation.</summary>
    public IReadOnlyList<SecretCollection> Collections { get; } = collections;

    /// <summary>Un item neuf peut-il être créé ? Sans collection, une organisation le refuserait.</summary>
    public bool CanCreateItems => Organisation is null || Collections.Count > 0;

    public Task<SecretWriteOutcome> WriteAsync(
        IReadOnlyList<SecretItemCreation> creations, string? collectionId, CancellationToken token) =>
        (_write ?? throw new ObjectDisposedException(nameof(SecretWriter)))(creations, collectionId, token);

    public void Forget() => _write = null;
}

/// <summary>
/// D'où viennent les valeurs des marqueurs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Extraite en préparation d'une seconde source</b> (Azure Key Vault a été évoqué), et pas
/// seulement pour elle : le cœur du rendu — <see cref="SecretTemplate"/>, <see cref="SecretBundle"/>
/// — ne connaissait déjà le coffre que par un <see cref="Func{T, TResult}"/>. L'interface ne fait
/// que <b>nommer</b> une frontière qui existait de fait, et rassembler derrière elle ce qui savait
/// « Bitwarden » et qui était éparpillé dans l'orchestrateur.
/// </para>
/// <para>
/// <b>Ce qu'elle ne prépare PAS, et il faut le dire.</b> L'authentification ne se généralise pas :
/// Bitwarden demande un mot de passe maître à chaque fois, Azure passe par
/// <c>DefaultAzureCredential</c> — navigateur, jeton mis en cache sur disque par une bibliothèque
/// qu'on ne contrôle pas, parfois MFA. Le paramètre <c>credential</c> ci-dessous est un mot de passe
/// <i>parce que la seule source implémentée en demande un</i> ; une source qui n'en veut pas
/// l'ignorerait, ce qui serait une abstraction qui ment. Cette moitié-là demandera une vraie
/// décision, pas une interface.
/// </para>
/// <para>
/// Ce qui est préparé, en revanche, c'est <b>toute la moitié résolution</b> — et c'est la plus
/// grosse.
/// </para>
/// </remarks>
public interface ISecretSource
{
    /// <summary>
    /// Identifiant stable de la source.
    /// </summary>
    /// <remarks>
    /// Destiné à devenir le <b>préfixe du marqueur</b> (<c>bw:</c>), pour que le fichier dise d'où
    /// viennent ses secrets — comme il dit déjà ce qu'on fait de lui. Un réglage global « quel
    /// coffre » qui laisserait le préfixe inchangé serait un mensonge dans le fichier.
    /// </remarks>
    string Id { get; }

    /// <summary>
    /// Peut-on demander sa clé à l'utilisateur ? <c>null</c> = oui.
    /// </summary>
    /// <remarks>
    /// Sert à ne pas réclamer un mot de passe maître quand il ne servirait à rien — outil absent, ou
    /// session jamais ouverte.
    /// </remarks>
    Task<SecretSourceFailure?> PreflightAsync(CancellationToken token);

    /// <summary>Ouvre le coffre et rend de quoi résoudre les marqueurs.</summary>
    /// <param name="credential">Ce que l'utilisateur a saisi. Ne doit jamais être journalisé.</param>
    /// <param name="refreshFirst">Rafraîchir la vue locale avant de lire, quand la source en a une.</param>
    Task<SecretSourceOpening> OpenAsync(string credential, bool refreshFirst, CancellationToken token);

    /// <summary>
    /// Date de la dernière mise à jour de la vue locale, ou <c>null</c> si la source n'en a pas.
    /// </summary>
    /// <remarks>
    /// Une source qui lit toujours en direct rend <c>null</c>, et l'écran n'affiche rien — plutôt
    /// qu'une date inventée qui ferait croire à un cache.
    /// </remarks>
    Task<DateTime?> LastRefreshAsync(CancellationToken token);

    /// <summary>Rafraîchit la vue locale à la demande. Sans objet pour une source sans cache.</summary>
    Task<SecretSourceFailure?> RefreshAsync(string credential, CancellationToken token);
}
