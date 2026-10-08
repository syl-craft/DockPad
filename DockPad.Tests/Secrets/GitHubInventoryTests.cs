using System.Globalization;
using DockPad.Secrets;
using DockPad.Services.Localization;

namespace DockPad.Tests.Secrets;

/// <summary>
/// L'inventaire <c>.vault</c> : en-tête, variables, lignes, rendu strict et comparaison avec GitHub.
/// </summary>
public class GitHubInventoryTests
{
    public GitHubInventoryTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private const string Secrets = """
        # .github/stores.secrets.vault
        # dockpad: github-secrets repo=syl-craft/cadranote environment=stores
        FIREFOX_JWT_ISSUER={{ bw:web-store:FIREFOX_JWT_ISSUER }}
        EDGE_API_KEY={{ bw:web-store:EDGE_API_KEY }}
        """;

    private static GitHubInventory Parsed(string content)
    {
        var (inventory, failures) = GitHubInventory.Parse(content);
        Assert.Empty(failures);
        return inventory ?? throw new InvalidOperationException("Inventaire attendu.");
    }

    private static IReadOnlyList<string> Refused(string content)
    {
        var (inventory, failures) = GitHubInventory.Parse(content);
        Assert.Null(inventory);
        Assert.NotEmpty(failures);
        return failures;
    }

    // ───────────── Détection ─────────────

    [Fact]
    public void UnEnTeteDockPadDeclareUnInventaire()
    {
        Assert.True(GitHubInventory.Declares(Secrets));
        Assert.Equal(SecretMode.GitHub, SecretPlan.Of(Secrets));
    }

    [Fact]
    public void UnFichierAMarqueursSansEnTeteResteEnPressePapier()
    {
        const string env = "TOKEN={{ bw:ntfy:token }}";

        Assert.False(GitHubInventory.Declares(env));
        Assert.Equal(SecretMode.Clipboard, SecretPlan.Of(env));
    }

    [Fact]
    public void UnTypeInconnuEstRefuseEtNonRenduDansLePressePapier()
    {
        // Une faute de frappe dans l'en-tête ne doit pas faire basculer le fichier vers le
        // presse-papier : il porte des marqueurs, et c'est ce qui se serait passé en silence.
        const string content = """
            # dockpad: github-secret repo=a/b
            X={{ bw:i:f }}
            """;

        Assert.Equal(SecretMode.GitHub, SecretPlan.Of(content));
        Assert.Contains(Refused(content), f => f.Contains("github-secret"));
    }

    // ───────────── En-tête ─────────────

    [Fact]
    public void LitLeTypeLeDepotEtLEnvironnement()
    {
        var inventory = Parsed(Secrets);

        Assert.Equal(GitHubTargetKind.Secrets, inventory.Kind);
        Assert.Equal("syl-craft/cadranote", inventory.Repo);
        Assert.Equal("stores", inventory.Environment);
        Assert.Equal(["FIREFOX_JWT_ISSUER", "EDGE_API_KEY"], inventory.Entries.Select(e => e.Name));
    }

    [Fact]
    public void SansEnvironnementCibleLeDepot()
    {
        var inventory = Parsed("""
            # dockpad: github-variables repo=syl-craft/cadranote
            CHROME_EXTENSION_ID={{ bw:web-store:CHROME_EXTENSION_ID }}
            """);

        Assert.Equal(GitHubTargetKind.Variables, inventory.Kind);
        Assert.Null(inventory.Environment);
    }

    [Fact]
    public void LeDepotEstObligatoire()
    {
        Refused("""
            # dockpad: github-secrets environment=stores
            X={{ bw:i:f }}
            """);
    }

    [Theory]
    [InlineData("syl-craft")]
    [InlineData("syl craft/x")]
    [InlineData("a/b/c")]
    public void UnDepotMalFormeEstRefuse(string repo)
    {
        Refused("# dockpad: github-secrets repo=" + repo + "\nX={{ bw:i:f }}");
    }

    [Fact]
    public void UnParametreInconnuEstRefuse()
    {
        Assert.Contains(Refused("""
            # dockpad: github-secrets repo=a/b env=stores
            X={{ bw:i:f }}
            """), f => f.Contains("env"));
    }

    // ───────────── Variables ─────────────

    [Fact]
    public void LesVariablesSeCitentDansLEnTeteEtDansLesMarqueurs()
    {
        var inventory = Parsed("""
            # dockpad: github-variables repo=${owner}/${project} environment=stores
            @owner = syl-craft
            @project = cadranote
            @item = syl-craft-web-store-apps
            CHROME_EXTENSION_ID={{ bw:${item}:${project}-CHROME_EXTENSION_ID }}
            """);

        Assert.Equal("syl-craft/cadranote", inventory.Repo);
        Assert.Equal("{{ bw:syl-craft-web-store-apps:cadranote-CHROME_EXTENSION_ID }}", inventory.Entries[0].Template);
        Assert.Equal([new SecretMarker("syl-craft-web-store-apps", "cadranote-CHROME_EXTENSION_ID")],
            SecretTemplate.FindMarkers(inventory.MarkersText));
    }

    [Fact]
    public void UneVariablePeutCiterUneVariableDejaDefinie()
    {
        var inventory = Parsed("""
            # dockpad: github-secrets repo=${repo}
            @owner = syl-craft
            @repo = ${owner}/cadranote
            X={{ bw:i:f }}
            """);

        Assert.Equal("syl-craft/cadranote", inventory.Repo);
    }

    [Fact]
    public void UneVariableInconnueEstRefuseeEtNommee()
    {
        Assert.Contains(Refused("""
            # dockpad: github-secrets repo=a/b
            X={{ bw:${item}:f }}
            """), f => f.Contains("item"));
    }

    [Fact]
    public void UneVariableRedefinieEstRefusee()
    {
        Refused("""
            # dockpad: github-secrets repo=a/b
            @item = un
            @item = deux
            X={{ bw:${item}:f }}
            """);
    }

    [Fact]
    public void UneVariableNePortePasDeMarqueur()
    {
        // Une variable est un littéral : la laisser porter un marqueur ferait d'elle une seconde
        // façon d'écrire une valeur du coffre, résolue on ne sait plus où.
        Refused("""
            # dockpad: github-secrets repo=a/b
            @valeur = {{ bw:i:f }}
            X=${valeur}
            """);
    }

    // ───────────── Lignes ─────────────

    [Fact]
    public void UneValeurEnClairEstRefusee()
    {
        Assert.Contains(Refused("""
            # dockpad: github-variables repo=a/b
            PUBLIC_ID=abc123
            """), f => f.Contains("PUBLIC_ID"));
    }

    [Theory]
    [InlineData("1ABC")]
    [InlineData("MON-NOM")]
    [InlineData("MON NOM")]
    public void UnNomInvalidePourGitHubEstRefuse(string name)
    {
        Refused("# dockpad: github-secrets repo=a/b\n" + name + "={{ bw:i:f }}");
    }

    [Fact]
    public void LePrefixeGitHubEstReserve()
    {
        Refused("""
            # dockpad: github-secrets repo=a/b
            GITHUB_TOKEN={{ bw:i:f }}
            """);
    }

    [Fact]
    public void UnDoublonEstRefuseSansEgardALaCasse()
    {
        // GitHub met les noms de secrets en majuscules : deux lignes de casse différente visent le
        // même secret, et la seconde écraserait la première sans le dire.
        Refused("""
            # dockpad: github-secrets repo=a/b
            TOKEN={{ bw:i:f }}
            token={{ bw:i:g }}
            """);
    }

    [Fact]
    public void UnInventaireSansLigneEstRefuse()
    {
        Refused("# dockpad: github-secrets repo=a/b");
    }

    [Fact]
    public void LesCommentairesEtLignesVidesSontIgnores()
    {
        var inventory = Parsed("""
            # dockpad: github-secrets repo=a/b

            # la clé du compte de service
            KEY={{ bw:i:notes }}
            """);

        Assert.Single(inventory.Entries);
    }

    // ───────────── Rendu strict ─────────────

    [Fact]
    public void RendChaqueLigneAvecSaValeur()
    {
        var inventory = Parsed(Secrets);

        var (values, missing) = inventory.Render(m => SecretLookup.Found($"v-{m.Field}"));

        Assert.Empty(missing);
        Assert.Equal(["FIREFOX_JWT_ISSUER", "EDGE_API_KEY"], values!.Select(v => v.Name));
        Assert.Equal("v-EDGE_API_KEY", values![1].Value);
    }

    [Fact]
    public void UnSeulMarqueurNonResoluBloqueToutLInventaire()
    {
        var inventory = Parsed(Secrets);

        var (values, missing) = inventory.Render(m => m.Field == "EDGE_API_KEY"
            ? SecretLookup.Missing("absent")
            : SecretLookup.Found("ok"));

        Assert.Null(values);
        Assert.Equal(["absent"], missing);
    }

    [Fact]
    public void UneValeurSurPlusieursLignesEstTransmiseTelleQuelle()
    {
        var inventory = Parsed("""
            # dockpad: github-secrets repo=a/b
            KEY={{ bw:i:notes }}
            """);
        const string pem = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----\n";

        var (values, _) = inventory.Render(_ => SecretLookup.Found(pem));

        Assert.Equal(pem, values![0].Value);
    }

    // ───────────── Comparaison ─────────────

    [Fact]
    public void LaComparaisonSepareManquantsEnTropEtPresents()
    {
        var inventory = Parsed(Secrets);
        var updated = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var check = inventory.Compare([
            new GitHubRemoteEntry("edge_api_key", updated),
            new GitHubRemoteEntry("OLD_TOKEN", updated),
        ]);

        Assert.Equal(["FIREFOX_JWT_ISSUER"], check.Missing);
        Assert.Equal(["OLD_TOKEN"], check.Extra);
        Assert.Equal(["EDGE_API_KEY"], check.Present.Select(p => p.Name));
        Assert.Equal(updated, check.Present[0].UpdatedAt);
    }

    [Fact]
    public void LAgeSeCompteEnJoursEntiers()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(0, GitHubInventory.AgeInDays(now.AddHours(-3), now));
        Assert.Equal(38, GitHubInventory.AgeInDays(now.AddDays(-38).AddHours(-1), now));
        Assert.Null(GitHubInventory.AgeInDays(null, now));
    }
}
