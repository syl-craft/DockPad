using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Lecture des annotations <c>x-bw</c> du bloc <c>secrets:</c> d'un docker-compose.
/// </summary>
/// <remarks>
/// <c>x-</c> est le mécanisme d'extension prévu par la spécification Compose : Compose ignore ces
/// champs, l'outil les lit.
/// </remarks>
public class ComposeSecretsTests
{
    public ComposeSecretsTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private const string Compose = """
        secrets:

          vw-ts-authkey:
            file: /share/CACHEDEV1_DATA/vaultwarden/secrets/ts-authkey
            x-bw:
              item: vaultwarden-infra
              field: ts-authkey

          vw-smtp-password:
            file: /share/CACHEDEV1_DATA/vaultwarden/secrets/smtp-password
            x-bw:
              item: vaultwarden-infra
              field: smtp-password

        services:
          vaultwarden:
            image: vaultwarden/server:1.37.1-alpine
        """;

    [Fact]
    public void LitChaqueSecretAnnote()
    {
        var (entries, failures, _) = ComposeSecrets.Extract(Compose);

        Assert.Empty(failures);
        Assert.Equal(["vw-ts-authkey", "vw-smtp-password"], entries.Select(e => e.Key));
    }

    [Fact]
    public void LeNomDuFichierEstLeNomDeBaseDeFile_PasLaCleDuSecret()
    {
        // `file:` est un chemin du NAS, inutilisable tel quel sur le poste. Et le nom de base
        // diffère de la clé du secret par le préfixe « vw- » : les confondre casserait le
        // déploiement en silence, Compose cherchant un fichier qui n'existe pas.
        var (entries, _, _) = ComposeSecrets.Extract(Compose);

        Assert.Equal(["ts-authkey", "smtp-password"], entries.Select(e => e.FileName));
    }

    [Fact]
    public void PorteLItemEtLeChampDuCoffre()
    {
        var (entries, _, _) = ComposeSecrets.Extract(Compose);

        Assert.Equal(new SecretMarker("vaultwarden-infra", "ts-authkey"), entries[0].Marker);
    }

    [Fact]
    public void UnSecretSansAnnotation_EstIgnore()
    {
        // Un secret dont la valeur ne vient pas du coffre n'est pas une erreur : il n'est
        // simplement pas de notre ressort.
        var (entries, failures, _) = ComposeSecrets.Extract("""
            secrets:
              externe:
                file: /ailleurs/valeur
              vw-token:
                file: /share/secrets/token
                x-bw:
                  item: infra
                  field: token
            """);

        Assert.Empty(failures);
        Assert.Equal("vw-token", Assert.Single(entries).Key);
    }

    [Fact]
    public void UneAnnotationSansFile_EstUnEchecNomme()
    {
        // Sans `file:`, aucun nom de fichier à produire : il n'y a rien à écrire, et le taire
        // laisserait croire que le secret a été traité.
        var (entries, failures, _) = ComposeSecrets.Extract("""
            secrets:
              vw-token:
                x-bw:
                  item: infra
                  field: token
            """);

        Assert.Empty(entries);
        Assert.Equal([Loc.F("Inject_Error_SecretNoFile", "vw-token")], failures);
    }

    [Fact]
    public void UneAnnotationSansItemNiChamp_EstUnEchecNomme()
    {
        var (_, failures, _) = ComposeSecrets.Extract("""
            secrets:
              vw-token:
                file: /share/secrets/token
                x-bw:
                  field: token
            """);

        Assert.Equal([Loc.F("Inject_Error_SecretIncomplete", "vw-token")], failures);
    }

    [Fact]
    public void UnDocumentSansBlocSecrets_NeDonneRien()
    {
        var (entries, failures, _) = ComposeSecrets.Extract("services:\n  web:\n    image: nginx");

        Assert.Empty(entries);
        Assert.Empty(failures);
    }

    [Fact]
    public void UneMentionDeXBwDansUnCommentaire_NeComptePas()
    {
        // Le compose réel documente le mécanisme en commentaire : « Chaque entrée porte un x-bw:
        // qui indique où trouver la valeur ». Une détection textuelle basculerait en mode fichiers
        // sur un fichier qui n'a aucune annotation.
        var (entries, failures, _) = ComposeSecrets.Extract("""
            # Chaque entrée porte un `x-bw:` qui indique où trouver la valeur.
            services:
              web:
                image: nginx
            """);

        Assert.Empty(entries);
        Assert.Empty(failures);
    }

    [Fact]
    public void UnYamlIllisible_EstRapporteAPart_PasCommeUneAnnotationFautive()
    {
        // Séparé des échecs d'annotation, parce que les deux ne disent pas la même chose : une
        // annotation fautive prouve qu'on est bien sur un compose à générer ; un document illisible
        // ne prouve rien — un .env ou un .png n'est pas du YAML, et ce n'est pas une panne.
        var (entries, failures, yamlError) = ComposeSecrets.Extract("secrets:\n  - [invalide\n");

        Assert.Empty(entries);
        Assert.Empty(failures);
        Assert.NotNull(yamlError);
    }

    [Fact]
    public void SupporteUnScalaireBlocAilleursDansLeDocument()
    {
        // Le compose réel porte un entrypoint de 90 lignes avec des `$$` : c'est la raison de
        // passer par un vrai parseur plutôt que par un balayage maison.
        var (entries, failures, _) = ComposeSecrets.Extract("""
            secrets:
              vw-token:
                file: /share/secrets/token
                x-bw:
                  item: infra
                  field: token
            services:
              backup:
                entrypoint:
                  - /bin/sh
                  - -c
                  - |
                    set -eu
                    H=$${BACKUP_HOUR:-1}
                    log() { echo "$$(date -u) $$*"; }
            """);

        Assert.Empty(failures);
        Assert.Single(entries);
    }
    // ───────────── Pièces jointes et sélecteur ─────────────

    [Fact]
    public void AttachmentEtSelect_DonnentUnMarqueurDePieceJointe()
    {
        var (entries, failures, _) = ComposeSecrets.Extract("""
            secrets:
              chrome-key:
                file: /share/secrets/chrome-key
                x-bw:
                  item: web-store
                  attachment: publisher.json
                  select: private_key
            """);

        Assert.Empty(failures);
        Assert.Equal(new SecretMarker("web-store", "@publisher.json|json:private_key"), Assert.Single(entries).Marker);
    }

    [Fact]
    public void AttachmentSeul_DonneLaPieceJointeEntiere()
    {
        var (entries, _, _) = ComposeSecrets.Extract("""
            secrets:
              config:
                file: /share/secrets/config.json
                x-bw:
                  item: infra
                  attachment: config.json
            """);

        Assert.Equal(new SecretMarker("infra", "@config.json"), Assert.Single(entries).Marker);
    }

    [Fact]
    public void FieldEtSelect_DonnentUnSelecteurSurLeChamp()
    {
        var (entries, _, _) = ComposeSecrets.Extract("""
            secrets:
              db-password:
                file: /share/secrets/db-password
                x-bw:
                  item: infra
                  field: notes
                  select: database.password
            """);

        Assert.Equal(new SecretMarker("infra", "notes|json:database.password"), Assert.Single(entries).Marker);
    }

    [Fact]
    public void FieldEtAttachmentEnsemble_SontRefuses()
    {
        var (entries, failures, _) = ComposeSecrets.Extract("""
            secrets:
              conflit:
                file: /share/secrets/conflit
                x-bw:
                  item: infra
                  field: token
                  attachment: token.txt
            """);

        Assert.Empty(entries);
        Assert.Equal([Loc.F("Inject_Error_SecretFieldAndAttachment", "conflit")], failures);
    }

    /// <summary>
    /// Un <c>select</c> qui n'est pas un texte non vide ne doit pas disparaître : sans lui, c'est le
    /// document entier qui serait écrit. Même règle pour <c>field</c> et <c>attachment</c>.
    /// </summary>
    [Theory]
    [InlineData("attachment: a.json\n      select: [private_key]", "select")]
    [InlineData("attachment: a.json\n      select: \"\"", "select")]
    [InlineData("field: token\n      attachment: [a.json]", "attachment")]
    [InlineData("field: [token]", "field")]
    public void UneAnnotationQuiNEstPasUnTexte_EstRefusee(string annotation, string key)
    {
        var (entries, failures, _) = ComposeSecrets.Extract($"""
            secrets:
              cle:
                file: /share/secrets/cle
                x-bw:
                  item: infra
                  {annotation}
            """);

        Assert.Empty(entries);
        Assert.Equal([Loc.F("Inject_Error_SecretBadAnnotation", "cle", key)], failures);
    }

    [Fact]
    public void TemplateAvecAttachmentOuSelect_ResteExclusif()
    {
        var (entries, failures, _) = ComposeSecrets.Extract("""
            secrets:
              a:
                file: /share/secrets/a
                x-bw:
                  item: infra
                  attachment: a.json
                  template: templates/a.yml
              b:
                file: /share/secrets/b
                x-bw:
                  template: templates/b.yml
                  select: k
            """);

        Assert.Empty(entries);
        Assert.Equal([Loc.F("Inject_Error_SecretBothSources", "a"), Loc.F("Inject_Error_SecretBothSources", "b")], failures);
    }
}
