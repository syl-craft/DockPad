namespace DockPad.Secrets;

/// <summary>Ce que le coffre répond à un marqueur, avant toute valeur.</summary>
/// <remarks>
/// <see cref="NotCreatable"/> : un marqueur qui vise une pièce jointe ou extrait une propriété JSON
/// n'est pas résolu, et le formulaire de création ne saurait pas l'écrire.
/// </remarks>
public enum SecretPresenceKind { Found, ItemMissing, FieldMissing, Ambiguous, NotCreatable }

/// <summary>
/// Une pièce jointe à télécharger : l'item qui la porte et son identifiant, jamais son nom.
/// </summary>
public readonly record struct SecretAttachmentRead(string ItemId, string AttachmentId);

/// <summary>
/// La présence d'un marqueur, et l'item qui le porte quand il est unique.
/// </summary>
/// <remarks>
/// <see cref="SecretPresenceKind.Ambiguous"/> ne porte pas d'identifiant : écrire dans l'un des
/// deux au hasard serait pire que de ne rien proposer.
/// </remarks>
public readonly record struct SecretPresence(SecretPresenceKind Kind, string? ItemId);

/// <summary>
/// L'instantané du coffre, et la seule chose qui répond à un marqueur.
/// </summary>
/// <remarks>
/// <para>
/// Pur : construit à partir d'une liste d'items déjà lue, sans jamais toucher à la CLI. C'est ce
/// qui permet de vérifier les trois cas d'échec — item absent, item en double, champ vide — sans
/// coffre ni réseau.
/// </para>
/// <para>
/// <b>Un seul <c>bw list items</c> alimente tout.</b> Le script d'origine lançait une recherche par
/// item ; ramener l'organisation entière en un appel est plus rapide, et surtout déplace la
/// décision du côté testable de la frontière. C'est aussi ce qui permet de nommer l'ambiguïté
/// nous-mêmes, là où la CLI se contentait d'un « More than one result » qui ne dit pas quoi
/// renommer.
/// </para>
/// </remarks>
/// <param name="attachmentContents">
/// Le contenu des pièces jointes téléchargées, par identifiant de pièce jointe.
/// </param>
public sealed class SecretVault(
    IReadOnlyList<BwItem> items, string organisation, IReadOnlyDictionary<string, string>? attachmentContents = null)
{
    /// <summary>
    /// Au-delà, une pièce jointe est refusée avant d'être téléchargée — même plafond qu'un gabarit.
    /// </summary>
    public const long MaxAttachmentBytes = 4 * 1024 * 1024;

    private const char ReplacementCharacter = (char)0xFFFD;
    private const char NullCharacter = (char)0;
    private const char ByteOrderMark = (char)0xFEFF;

    public IReadOnlyDictionary<string, string> AttachmentContents { get; } =
        attachmentContents ?? new Dictionary<string, string>();

    public SecretPresence Classify(SecretMarker marker)
    {
        var matches = Matches(marker);

        if (matches.Count > 1) return new(SecretPresenceKind.Ambiguous, null);

        if (marker.Reference.IsPlain == false)
            return Lookup(marker).Value is { Length: > 0 }
                ? new(SecretPresenceKind.Found, matches[0].Id)
                : new(SecretPresenceKind.NotCreatable, null);

        if (matches.Count == 0) return new(SecretPresenceKind.ItemMissing, null);

        var value = SecretFieldResolver.Resolve(matches[0], marker.Field);

        return new(string.IsNullOrEmpty(value) ? SecretPresenceKind.FieldMissing : SecretPresenceKind.Found,
            matches[0].Id);
    }

    public SecretLookup Lookup(SecretMarker marker)
    {
        var matches = Matches(marker);

        if (matches.Count == 0)
            return SecretLookup.Missing(string.IsNullOrWhiteSpace(organisation)
                ? Loc.F("Inject_Error_ItemMissingVault", marker.Item)
                : Loc.F("Inject_Error_ItemMissingOrg", marker.Item, organisation));

        if (matches.Count > 1)
            return SecretLookup.Missing(Loc.F("Inject_Error_ItemAmbiguous", marker.Item));

        var reference = marker.Reference;
        if (reference.HasUnknownFilter)
            return SecretLookup.Missing(Loc.F("Inject_Error_UnknownFilter", marker.Item, marker.Field));

        var source = reference.IsAttachment
            ? ReadAttachment(matches[0], marker.Item, reference.Name)
            : FieldValue(matches[0], marker.Item, reference.Name);

        if (source.Value is not { } document || reference.JsonPath is not { } path) return source;

        return SelectJson(marker.Item, reference, path, document);
    }

    /// <summary>
    /// Les pièces jointes que les marqueurs citent et qu'il faut télécharger : une par nom résolu
    /// sans ambiguïté, sous le plafond de taille.
    /// </summary>
    public IReadOnlyList<SecretAttachmentRead> AttachmentsToRead(IEnumerable<SecretMarker> markers)
    {
        var reads = new List<SecretAttachmentRead>();

        foreach (var marker in markers)
        {
            var reference = marker.Reference;
            if (reference.IsAttachment == false) continue;

            var matches = Matches(marker);
            if (matches.Count != 1) continue;

            var candidates = AttachmentsNamed(matches[0], reference.Name);
            if (candidates.Count != 1 || IsTooBig(candidates[0])) continue;

            reads.Add(new SecretAttachmentRead(matches[0].Id, candidates[0].Id));
        }

        return reads.Distinct().ToList();
    }

    private static SecretLookup FieldValue(BwItem item, string itemName, string field)
    {
        var value = SecretFieldResolver.Resolve(item, field);

        return string.IsNullOrEmpty(value)
            ? SecretLookup.Missing(Loc.F("Inject_Error_EmptyField", itemName, field))
            : SecretLookup.Found(value);
    }

    private SecretLookup ReadAttachment(BwItem item, string itemName, string fileName)
    {
        var candidates = AttachmentsNamed(item, fileName);

        if (candidates.Count == 0)
            return SecretLookup.Missing(Loc.F("Inject_Error_AttachmentMissing", itemName, fileName));

        if (candidates.Count > 1)
            return SecretLookup.Missing(Loc.F("Inject_Error_AttachmentAmbiguous", itemName, fileName));

        if (IsTooBig(candidates[0]))
            return SecretLookup.Missing(Loc.F("Inject_Error_AttachmentTooBig",
                itemName, fileName, MaxAttachmentBytes / (1024 * 1024)));

        if (AttachmentContents.TryGetValue(candidates[0].Id, out var content) == false)
            return SecretLookup.Missing(Loc.F("Inject_Error_AttachmentUnreadable", itemName, fileName));

        // La sortie de la CLI est décodée en UTF-8 : un octet invalide y devient U+FFFD.
        if (content.Contains(ReplacementCharacter) || content.Contains(NullCharacter))
            return SecretLookup.Missing(Loc.F("Inject_Error_AttachmentBinary", itemName, fileName));

        var text = content.TrimStart(ByteOrderMark);

        return text.Length == 0
            ? SecretLookup.Missing(Loc.F("Inject_Error_EmptyField", itemName, "@" + fileName))
            : SecretLookup.Found(text);
    }

    /// <summary>
    /// Les échecs nomment l'item, la source et le chemin, jamais un fragment du document.
    /// </summary>
    private static SecretLookup SelectJson(string itemName, SecretFieldReference reference, string path, string document)
    {
        var selection = SecretJsonSelector.Select(document, path);
        var source = reference.IsAttachment ? "@" + reference.Name : reference.Name;

        if (selection.Value is { } value) return SecretLookup.Found(value);

        return selection.Failure == SecretJsonFailure.InvalidJson
            ? SecretLookup.Missing(Loc.F("Inject_Error_JsonInvalid", itemName, source))
            : SecretLookup.Missing(Loc.F("Inject_Error_JsonNoValue", itemName, source, path));
    }

    private static List<BwAttachment> AttachmentsNamed(BwItem item, string fileName) => (item.Attachments ?? [])
        .Where(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private static bool IsTooBig(BwAttachment attachment) => attachment.Size > MaxAttachmentBytes;

    private List<BwItem> Matches(SecretMarker marker) => items
        .Where(i => string.Equals(i.Name, marker.Item, StringComparison.OrdinalIgnoreCase))
        .ToList();
}
