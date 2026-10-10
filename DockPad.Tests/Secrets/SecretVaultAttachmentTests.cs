using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Pièces jointes et sélecteur JSON vus par le coffre : ce qu'il faut télécharger, ce que rend un
/// marqueur (trouvée, absente, ambiguë, trop grosse, binaire, BOM retiré), ce que les échecs ne
/// recopient jamais, et le rendu de bout en bout (presse-papier, fichiers, inventaire GitHub).
/// </summary>
public class SecretVaultAttachmentTests
{
    public SecretVaultAttachmentTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private const string Sentinel = "SENTINELLE-NE-JAMAIS-AFFICHER";

    private const string ServiceAccount =
        "{ \"type\": \"service_account\", \"private_key\": \"-----BEGIN PRIVATE KEY-----\\nMIIE\\n-----END PRIVATE KEY-----\\n\" }";

    private static BwItem Item(string id, string name, params BwAttachment[] attachments) => new()
    {
        Id = id,
        Name = name,
        Attachments = attachments.ToList(),
    };

    private static BwAttachment Attachment(string id, string fileName, long size = 100) =>
        new() { Id = id, FileName = fileName, Size = size };

    private static SecretVault Vault(IReadOnlyDictionary<string, string> contents, params BwItem[] items) =>
        new(items, "", contents);

    private static SecretVault StoreVault() => Vault(
        new Dictionary<string, string> { ["att1"] = ServiceAccount },
        Item("i1", "web-store", Attachment("att1", "publisher.json")));

    // ───────────── Ce qu'il faut télécharger ─────────────

    [Fact]
    public void SeulesLesPiecesJointesCitees_SontATelecharger()
    {
        var vault = Vault(new Dictionary<string, string>(),
            Item("i1", "web-store", Attachment("att1", "publisher.json"), Attachment("att2", "other.json")));

        var reads = vault.AttachmentsToRead([
            new("web-store", "@PUBLISHER.JSON|json:private_key"),
            new("web-store", "@publisher.json"),
            new("web-store", "password"),
        ]);

        Assert.Equal([new SecretAttachmentRead("i1", "att1")], reads);
    }

    [Fact]
    public void UnePieceJointeTropGrosse_NEstPasTelechargee()
    {
        var vault = Vault(new Dictionary<string, string>(),
            Item("i1", "infra", Attachment("big", "dump.json", SecretVault.MaxAttachmentBytes + 1)));

        Assert.Empty(vault.AttachmentsToRead([new("infra", "@dump.json")]));
        Assert.Contains("dump.json", vault.Lookup(new("infra", "@dump.json")).Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public void UneTailleInconnueOuInvalide_NEstPasTelechargee(long? size)
    {
        var vault = Vault(new Dictionary<string, string>(),
            Item("i1", "infra", new BwAttachment { Id = "a1", FileName = "config.json", Size = size }));

        Assert.Empty(vault.AttachmentsToRead([new("infra", "@config.json")]));
        Assert.Equal(Loc.F("Inject_Error_AttachmentSizeUnknown", "infra", "config.json"),
            vault.Lookup(new("infra", "@config.json")).Failure);
    }

    [Fact]
    public void UnePieceJointeAmbigue_NEstPasTelechargee()
    {
        var vault = Vault(new Dictionary<string, string>(),
            Item("i1", "infra", Attachment("a1", "config.json"), Attachment("a2", "CONFIG.json")));

        Assert.Empty(vault.AttachmentsToRead([new("infra", "@config.json")]));
    }

    // ───────────── Ce que rend un marqueur ─────────────

    [Fact]
    public void LaPieceJointeEntiere_EstRendue()
    {
        Assert.Equal(ServiceAccount, StoreVault().Lookup(new("web-store", "@Publisher.json")).Value);
    }

    [Fact]
    public void UnePropriete_EstExtraiteDeLaPieceJointe()
    {
        var found = StoreVault().Lookup(new("web-store", "@publisher.json|json:private_key"));

        Assert.Equal("-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n", found.Value);
    }

    [Fact]
    public void UnePropriete_EstExtraiteDUnChampPersonnalise()
    {
        var item = new BwItem
        {
            Id = "i1",
            Name = "infra",
            Fields = [new BwField { Name = "config", Value = "{ \"servers\": [ { \"host\": \"nas\" } ] }" }],
        };

        Assert.Equal("nas", new SecretVault([item], "").Lookup(new("infra", "config|json:servers.0.host")).Value);
    }

    [Fact]
    public void UnePieceJointeAbsente_EstNommee()
    {
        var failure = StoreVault().Lookup(new("web-store", "@absent.json")).Failure;

        Assert.Contains("web-store", failure);
        Assert.Contains("absent.json", failure);
    }

    [Fact]
    public void DeuxPiecesJointesDuMemeNom_SontUneAmbiguite()
    {
        var vault = Vault(new Dictionary<string, string> { ["a1"] = "{}", ["a2"] = "{}" },
            Item("i1", "infra", Attachment("a1", "config.json"), Attachment("a2", "config.json")));

        var failure = vault.Lookup(new("infra", "@config.json")).Failure;

        Assert.Equal(Loc.F("Inject_Error_AttachmentAmbiguous", "infra", "config.json"), failure);
    }

    [Fact]
    public void UnePieceJointeNonTelechargee_EstIllisible()
    {
        var vault = Vault(new Dictionary<string, string>(), Item("i1", "infra", Attachment("a1", "config.json")));

        Assert.Equal(Loc.F("Inject_Error_AttachmentUnreadable", "infra", "config.json"),
            vault.Lookup(new("infra", "@config.json")).Failure);
    }

    /// <summary>
    /// Caractère nul, ou caractère de remplacement U+FFFD laissé par un octet UTF-8 invalide.
    /// </summary>
    [Theory]
    [InlineData(0x0000)]
    [InlineData(0xFFFD)]
    public void UnContenuBinaire_EstRefuse(int codePoint)
    {
        var content = "PK" + (char)codePoint + "data";
        var vault = Vault(new Dictionary<string, string> { ["a1"] = content },
            Item("i1", "infra", Attachment("a1", "archive.zip")));

        var found = vault.Lookup(new("infra", "@archive.zip"));

        Assert.Null(found.Value);
        Assert.Equal(Loc.F("Inject_Error_AttachmentBinary", "infra", "archive.zip"), found.Failure);
    }

    [Fact]
    public void LeBomDeTete_EstRetire()
    {
        var vault = Vault(new Dictionary<string, string> { ["a1"] = (char)0xFEFF + "{ \"k\": \"v\" }" },
            Item("i1", "infra", Attachment("a1", "config.json")));

        Assert.Equal("{ \"k\": \"v\" }", vault.Lookup(new("infra", "@config.json")).Value);
        Assert.Equal("v", vault.Lookup(new("infra", "@config.json|json:k")).Value);
    }

    [Fact]
    public void UnFiltreInconnu_EchoueSansLireLaValeur()
    {
        var failure = StoreVault().Lookup(new("web-store", "@publisher.json|yaml:k")).Failure;

        Assert.Equal(Loc.F("Inject_Error_UnknownFilter", "web-store", "@publisher.json|yaml:k"), failure);
    }

    // ───────────── Ce que les échecs ne recopient jamais ─────────────

    [Theory]
    [InlineData("{ \"k\": \"" + Sentinel + "\" ", "k")]
    [InlineData("{ \"k\": { \"inner\": \"" + Sentinel + "\" } }", "k")]
    [InlineData("{ \"k\": [\"" + Sentinel + "\"] }", "k")]
    [InlineData("{ \"k\": \"" + Sentinel + "\" }", "absent")]
    [InlineData(Sentinel, "k")]
    public void UnEchecDExtraction_NeContientAucunFragmentDeLaValeur(string json, string path)
    {
        var vault = Vault(new Dictionary<string, string> { ["a1"] = json },
            Item("i1", "infra", Attachment("a1", "config.json")));

        var found = vault.Lookup(new("infra", $"@config.json|json:{path}"));

        Assert.Null(found.Value);
        Assert.NotNull(found.Failure);
        Assert.DoesNotContain(Sentinel, found.Failure);
        Assert.Contains("config.json", found.Failure);
    }

    // ───────────── Le formulaire de création ─────────────

    [Theory]
    [InlineData("@absent.json")]
    [InlineData("@publisher.json|json:absent")]
    [InlineData("password|json:k")]
    public void UnMarqueurAvecPieceJointeOuSelecteur_NEstJamaisCreable(string field)
    {
        var vault = StoreVault();

        Assert.Equal(SecretPresenceKind.NotCreatable, vault.Classify(new("web-store", field)).Kind);
        Assert.Empty(SecretCreationPlan.Build([new SecretMarker("web-store", field)], vault.Classify));
    }

    [Fact]
    public void UnItemAbsent_AvecSelecteur_NEstPasProposeNonPlus()
    {
        var vault = StoreVault();

        Assert.Equal(SecretPresenceKind.NotCreatable, vault.Classify(new("nouvel-item", "@a.json")).Kind);
        Assert.Empty(SecretCreationPlan.Build([new SecretMarker("nouvel-item", "notes|json:k")], vault.Classify));
    }

    [Fact]
    public void UnePieceJointeTrouvee_EstFound()
    {
        Assert.Equal(SecretPresenceKind.Found,
            StoreVault().Classify(new("web-store", "@publisher.json|json:private_key")).Kind);
    }

    // ───────────── Rendu de bout en bout ─────────────

    private const string ExpectedKey = "-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n";

    [Fact]
    public void PressePapier_LaCleExtraiteSortAvecDeVraisRetoursALaLigne()
    {
        var result = SecretTemplate.Render(
            "KEY={{ bw:web-store:@publisher.json|json:private_key }}", StoreVault().Lookup);

        Assert.True(result.Complete);
        Assert.Equal("KEY=" + ExpectedKey, result.Text);
    }

    [Fact]
    public void Fichiers_LAnnotationAttachmentSelect_EcritLaCle()
    {
        const string compose = """
            secrets:
              chrome-key:
                file: /share/secrets/chrome-key
                x-bw:
                  item: web-store
                  attachment: publisher.json
                  select: private_key
            """;

        var scan = ComposeSecrets.Extract(compose);
        var bundle = SecretBundle.Resolve(scan.Entries, StoreVault().Lookup);

        Assert.True(bundle.Complete);
        Assert.Equal(ExpectedKey, Assert.Single(bundle.Files).Value);
    }

    [Fact]
    public void InventaireGitHub_LaLigneRendLaCleExtraite()
    {
        var (inventory, failures) = GitHubInventory.Parse("""
            # github-secrets repo=syl-craft/cadranote environment=stores
            CHROME_SERVICE_ACCOUNT_PRIVATE_KEY={{ bw:web-store:@publisher.json|json:private_key }}
            """);
        Assert.Empty(failures);
        Assert.NotNull(inventory);

        var (values, missing) = inventory.Render(StoreVault().Lookup);

        Assert.Empty(missing);
        Assert.NotNull(values);
        Assert.Equal(ExpectedKey, Assert.Single(values).Value);
    }

    [Fact]
    public void UnMarqueurEchappe_ResteLitteral()
    {
        var result = SecretTemplate.Render(
            "doc: \\{{ bw:x:@a.json|json:k }}\nKEY={{ bw:web-store:@publisher.json|json:private_key }}",
            StoreVault().Lookup);

        Assert.True(result.Complete);
        Assert.StartsWith("doc: {{ bw:x:@a.json|json:k }}\n", result.Text);
    }
}
