using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Ce qu'un lancement de DockPad a le droit de relayer avant que WPF n'existe.
/// </summary>
/// <remarks>
/// <para>
/// Windows lance un <c>DockPad.exe</c> entier au clic sur un lien, pour un travail qui tient en une
/// ligne écrite dans un tube. Mesuré : ~300 ms, dont ~2 ms d'utile — le reste est le runtime .NET,
/// l'<c>Application</c> WPF et le prologue de <c>OnStartup</c>. Décider <b>avant</b> tout ça demande
/// une fonction qui ne dépende de rien, et c'est celle-ci.
/// </para>
/// <para>
/// <b>Le silence est le mode de panne à craindre.</b> Un nom de tube erroné ne lève pas : le
/// raccourci échoue, on retombe sur le chemin lent, et tout continue de marcher — simplement plus
/// lentement, pour toujours, sans un mot. La parade n'est pas un test mais la <b>structure</b> :
/// les trois noms sont déclarés ici et les serveurs les lisent, si bien qu'ils ne peuvent pas
/// diverger. Un test qui comparerait la constante à elle-même ne prouverait rien.
/// </para>
/// </remarks>
public class StartupRelayTests
{
    [Fact]
    public void SansArgument_DemandeALInstanceResidenteDeSeMontrer()
    {
        var plan = StartupRelay.Plan([]);

        Assert.NotNull(plan);
        Assert.Equal(StartupRelay.ShowPipeName, plan.Value.PipeName);
        Assert.Equal(StartupRelay.ShowRequest, plan.Value.Payload);
    }

    [Fact]
    public void Url_PartSurLeTubeDesUrls()
    {
        var plan = StartupRelay.Plan(["--url", "https://github.com/x?a=1"]);

        Assert.NotNull(plan);
        Assert.Equal(StartupRelay.UrlPipeName, plan.Value.PipeName);
        Assert.Equal("https://github.com/x?a=1", plan.Value.Payload);
    }

    [Fact]
    public void InjectionDeSecrets_PartSurSonPropreTube()
    {
        var plan = StartupRelay.Plan(["--inject-secrets", @"C:\dev\infra\compose.yml"]);

        Assert.NotNull(plan);
        Assert.Equal(StartupRelay.InjectPipeName, plan.Value.PipeName);
        Assert.Equal(@"C:\dev\infra\compose.yml", plan.Value.Payload);
    }

    [Fact]
    public void Mcp_NeRelaieJamais()
    {
        // Le mode MCP est un serveur stdio : il DOIT démarrer, et il coexiste avec l'instance
        // résidente. Le relayer le rendrait muet, et Claude n'aurait plus d'outils.
        Assert.Null(StartupRelay.Plan(["--mcp"]));
    }

    [Fact]
    public void Mcp_LemporteMemeAccompagneDUneUrl()
    {
        Assert.Null(StartupRelay.Plan(["--mcp", "--url", "https://exemple.test"]));
    }

    [Fact]
    public void UrlSansValeur_RetombeSurLaDemandeDAffichage()
    {
        // Exactement ce que fait ParseArg aujourd'hui : un « --url » en dernière position ne
        // porte pas de valeur, donc le lancement vaut « montre-toi ». Le raccourci rapide ne doit
        // pas inventer un comportement que le chemin lent n'a pas.
        var plan = StartupRelay.Plan(["--url"]);

        Assert.NotNull(plan);
        Assert.Equal(StartupRelay.ShowPipeName, plan.Value.PipeName);
    }

    [Fact]
    public void ArgumentInconnu_ValutUneDemandeDAffichage()
    {
        // Même règle que le chemin lent : tout ce qui n'est ni --url ni --inject-secrets ni --mcp
        // aboutit à « un second lancement, remonte la fenêtre ».
        var plan = StartupRelay.Plan(["--peu-importe"]);

        Assert.NotNull(plan);
        Assert.Equal(StartupRelay.ShowPipeName, plan.Value.PipeName);
    }

    // ── La question posée avant le tube ──────────────────────────────────────

    [Fact]
    public void AucuneInstance_LeRaccourciSeTait()
    {
        // LE test qui compte. Sur un tube dont personne n'écoute, Connect attend TOUT son délai
        // avant d'échouer : si cette question répondait « oui » à tort, un clic sur un lien
        // DockPad éteint paierait deux secondes avant même de commencer à démarrer — et si elle
        // répondait « oui » toujours, l'application ne démarrerait plus du tout.
        Assert.False(StartupRelay.InstanceIsRunning($"DockPad_Absent_{Guid.NewGuid():N}"));
    }

    [Fact]
    public void MutexPresent_LeRaccourciSAutorise()
    {
        var name = $"DockPad_Test_{Guid.NewGuid():N}";
        using var held = new System.Threading.Mutex(initiallyOwned: true, name);

        Assert.True(StartupRelay.InstanceIsRunning(name));
    }

    [Fact]
    public void MutexRelache_LeRaccourciSeTaitDeNouveau()
    {
        // Le mutex disparaît avec le processus qui le tient : une instance fermée ne doit pas
        // laisser le raccourci croire qu'elle écoute encore.
        var name = $"DockPad_Test_{Guid.NewGuid():N}";
        var held = new System.Threading.Mutex(initiallyOwned: true, name);
        held.Dispose();

        Assert.False(StartupRelay.InstanceIsRunning(name));
    }
}
