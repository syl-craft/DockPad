using System.IO;

namespace DockPad.Services;

/// <summary>
/// Emplacement du profil de l'application (configs JSON, store d'icônes, logs) :
/// %APPDATA%\&lt;produit&gt; par défaut, ou le dossier indiqué par la variable d'environnement
/// &lt;PRODUIT&gt;_PROFILE_DIR. Sert aux profils portables et aux outils de capture, qui pointent un
/// dossier de fixture au lieu du profil de l'utilisateur.
/// Résolu une fois au premier accès : changer la variable ensuite n'a aucun effet.
/// </summary>
public static class AppPaths
{
    private static string? _product;
    private static string? _root;

    /// <summary>Produit par défaut, quand l'application n'a rien posé.</summary>
    private const string DefaultProduct = "DockPad";

    /// <summary>
    /// Nom du produit, posé par l'application <b>avant tout accès</b> aux chemins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plusieurs applications peuvent partager ce type et ne peuvent pas partager un profil.
    /// </para>
    /// <para>
    /// <b>La racine est résolue paresseusement</b>, et ce n'est pas un détail : en
    /// <c>static readonly</c>, elle serait calculée au <b>chargement du type</b> — or cet appel est
    /// un membre du même type, donc l'appeler déclencherait cette initialisation, qui lirait un nom
    /// de produit encore nul. Le mécanisme serait silencieusement inopérant, et la seconde
    /// application écrirait dans le profil de la première.
    /// </para>
    /// </remarks>
    public static void Initialize(string product)
    {
        // Un appel APRES la premiere lecture ne peut plus rien changer : la racine est figee, et le
        // second produit ecrirait dans le profil du premier. C'est exactement la panne que la
        // resolution paresseuse existe pour empecher, et la laisser passer en silence rendrait la
        // precaution inutile.
        //
        // Mais on ne crie que si ca CHANGE quelque chose. Les outils de capture posent leur profil
        // de fixture par variable d'environnement, lisent la racine, puis montent l'application qui
        // appelle Initialize : l'ordre est inverse, et pourtant les deux chemins menent au meme
        // dossier. Lever la serait du bruit, et la garde finirait par etre desarmee.
        if (_root is not null)
        {
            var wouldBe = Resolve(
                Environment.GetEnvironmentVariable(OverrideVariable),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ResolveProduct(product, DefaultProduct));

            if (!string.Equals(wouldBe, _root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"AppPaths.Initialize(\"{product}\") would resolve to '{wouldBe}', " +
                    $"but the profile root was already resolved to '{_root}'.");

            return;
        }

        _product = product;
    }

    /// <summary>Décision pure : le produit posé, ou le défaut.</summary>
    public static string ResolveProduct(string? initialized, string fallback) =>
        string.IsNullOrWhiteSpace(initialized) ? fallback : initialized;

    /// <summary>Variable d'environnement de surcharge, dérivée du produit.</summary>
    public static string OverrideVariable =>
        $"{ResolveProduct(_product, DefaultProduct).ToUpperInvariant()}_PROFILE_DIR";

    public static string ProfileRoot => _root ??= Resolve(
        Environment.GetEnvironmentVariable(OverrideVariable),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ResolveProduct(_product, DefaultProduct));

    /// <summary>
    /// Dossier de profil : la surcharge est prise telle quelle (aucun sous-dossier ajouté, pour que
    /// le dossier indiqué soit bien celui utilisé), sinon &lt;appData&gt;\&lt;produit&gt;.
    /// </summary>
    /// <remarks>
    /// Le paramètre <paramref name="product"/> porte un défaut : les tests existants appellent la
    /// forme à deux arguments et n'ont pas à changer.
    /// </remarks>
    public static string Resolve(string? overrideDir, string appData, string product = DefaultProduct)
    {
        var dir = overrideDir?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(dir)
            ? Path.Combine(appData, product)
            : Path.GetFullPath(dir);
    }

    /// <summary>Chemin d'un fichier du profil (ex. AppPaths.File("browsers.json")).</summary>
    public static string File(string name) => Path.Combine(ProfileRoot, name);
}
