using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using SmartFormat;
using SmartFormat.Core.Settings;

namespace DockPad.Services.Localization;

/// <summary>
/// Seule porte d'accès aux chaînes traduites, pour le XAML comme pour le code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune référence WPF ici.</b> C'est ce qui permet aux services, aux fournisseurs de
/// consommation et aux ViewModels d'être traduits tout en restant testables sans instance
/// <c>Application</c> — la même raison qui a fait sortir l'état du bandeau Usage du code-behind.
/// L'extension de balisage vit à part, dans <c>LocExtension</c>.
/// </para>
/// <para>
/// <b>Singleton plus façade statique.</b> La liaison XAML a besoin d'une instance qui notifie, le
/// code appelant n'a pas à la connaître : <see cref="Instance"/> pour la première,
/// <see cref="T"/>/<see cref="F"/> pour le second, un seul état de langue dans le processus.
/// </para>
/// <para>
/// <b>La bascule à chaud tient à une ligne</b> : <see cref="SetCulture"/> notifie
/// <see cref="IndexerName"/>, ce qui invalide d'un coup toutes les liaisons d'indexeur de
/// l'application. Il n'y a donc aucun abonnement à gérer fenêtre par fenêtre, et une fenêtre
/// ajoutée plus tard en bénéficie sans rien brancher.
/// </para>
/// </remarks>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>
    /// Nom de propriété que WPF écoute pour invalider une liaison d'indexeur. C'est la valeur de
    /// <c>System.Windows.Data.Binding.IndexerName</c>, recopiée pour ne pas dépendre de WPF ici —
    /// un test vérifie qu'elle est bien notifiée.
    /// </summary>
    private const string IndexerName = "Item[]";

    /// <summary>Langue neutre du magasin, et repli quand la culture demandée n'est pas traduite.</summary>
    private static readonly CultureInfo Neutral = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// Langue de Windows, capturée au chargement du type — donc <b>avant</b> le premier
    /// <see cref="SetCulture"/>.
    /// </summary>
    /// <remarks>
    /// Lire <c>CurrentUICulture</c> au moment du besoin serait faux : <see cref="SetCulture"/> écrit
    /// dedans, si bien que « automatique » aurait gardé la dernière langue choisie au lieu de revenir
    /// à celle du système. On garde <c>CurrentUICulture</c> plutôt que <c>InstalledUICulture</c> : la
    /// première porte la langue d'affichage choisie par l'utilisateur dans Windows, la seconde celle
    /// de l'installation, qui n'est pas ce qu'on veut suivre.
    /// </remarks>
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    private static readonly ResourceManager Resources =
        new("DockPad.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// Formateur SmartFormat, construit une fois. Il porte le pluriel CLDR et la conjonction de
    /// listes ; ses erreurs de gabarit lèvent plutôt que de passer inaperçues, un test parcourt
    /// donc toutes les valeurs de ressources pour les attraper avant l'écran.
    /// </summary>
    public static SmartFormatter Formatter { get; } = BuildFormatter();

    public static Loc Instance { get; } = new();

    private Loc() { }

    /// <summary>Culture d'affichage courante. Jamais nulle : « automatique » est déjà résolu.</summary>
    public static CultureInfo Current { get; private set; } = Resolve(null);

    /// <summary>Levé après un changement de langue, pour les rares vues sans liaison.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Chaîne traduite, ou <c>[clé]</c> si elle manque.</summary>
    public static string T(string key)
    {
        try
        {
            return Resources.GetString(key, Current) ?? $"[{key}]";
        }
        catch (MissingManifestResourceException)
        {
            // Le magasin lui-même est introuvable : afficher la clé vaut mieux qu'une fenêtre qui
            // ne s'ouvre pas.
            return $"[{key}]";
        }
    }

    /// <summary>
    /// Chaîne traduite dont le gabarit est appliqué par SmartFormat : placeholders, pluriel
    /// (<c>{0:plural:règle|règles}</c>) et listes.
    /// </summary>
    public static string F(string key, params object?[] args)
    {
        var template = T(key);
        try
        {
            // La culture est passée explicitement : Formatter.Format() sans culture prendrait celle
            // du thread, qui peut différer pendant une bascule.
            return Formatter.Format(Current, template, args);
        }
        catch (Exception)
        {
            // Un gabarit fautif ne doit pas faire tomber la fenêtre. Le test de parsing des
            // ressources est là pour que ce cas n'arrive jamais en production.
            return template;
        }
    }

    /// <summary>Texte pour la liaison XAML. Même vérité que <see cref="T"/>.</summary>
    public string this[string key] => T(key);

    /// <summary>
    /// Étiquette de langue → culture, <c>null</c> pour « automatique ». Une étiquette inconnue vaut
    /// automatique : la valeur vient du registre, qu'un utilisateur peut éditer à la main.
    /// </summary>
    public static CultureInfo? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag.Trim());
            return culture.TwoLetterISOLanguageName is "fr" or "en" ? culture : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Change la langue d'affichage. <c>null</c> = celle de Windows.
    /// </summary>
    /// <remarks>
    /// Les quatre affectations de culture sont nécessaires : les deux premières pour le thread
    /// courant, les deux <c>DefaultThread…</c> pour ceux qui n'existent pas encore — sans elles les
    /// <c>Task.Run</c> des fournisseurs de consommation formatent nombres et heures dans la culture
    /// d'origine du processus, sous une interface déjà traduite.
    /// </remarks>
    public static void SetCulture(CultureInfo? culture)
    {
        Current = Resolve(culture);

        CultureInfo.CurrentUICulture = Current;
        CultureInfo.CurrentCulture = Current;
        CultureInfo.DefaultThreadCurrentUICulture = Current;
        CultureInfo.DefaultThreadCurrentCulture = Current;

        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(IndexerName));
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Toutes les entrées d'une langue. Sert aux tests de parité et de gabarit — la contrepartie du
    /// choix de mettre de la syntaxe SmartFormat dans les valeurs.
    /// </summary>
    public static IEnumerable<(string Key, string Value)> AllEntries(CultureInfo culture)
    {
        // La langue neutre n'est pas une culture : « Strings.resx » sans suffixe produit la ressource
        // INVARIANTE. Demander « en » sans repli ne trouverait donc rien, et la parité échouerait en
        // annonçant que tout l'anglais manque.
        var target = culture.TwoLetterISOLanguageName == Neutral.TwoLetterISOLanguageName
            ? CultureInfo.InvariantCulture
            : culture;

        // tryParents à faux : on veut les entrées de CETTE langue seulement, sans le repli — sinon
        // le français hériterait de l'anglais et la parité serait vraie par construction.
        var set = Resources.GetResourceSet(target, createIfNotExists: true, tryParents: false);
        if (set is null) yield break;

        foreach (DictionaryEntry entry in set)
        {
            if (entry.Key is string key && entry.Value is string value)
                yield return (key, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static CultureInfo Resolve(CultureInfo? culture)
    {
        var candidate = culture ?? SystemCulture;
        return candidate.TwoLetterISOLanguageName is "fr" or "en" ? candidate : Neutral;
    }

    private static SmartFormatter BuildFormatter()
    {
        var formatter = Smart.CreateDefaultSmartFormat(new SmartSettings
        {
            // Lever sur un gabarit fautif plutôt que l'afficher tel quel : F() attrape et rend le
            // gabarit brut, et le test de parsing transforme la faute en échec de suite de tests.
            Parser = new ParserSettings { ErrorAction = ParseErrorAction.ThrowError },
            Formatter = new FormatterSettings { ErrorAction = FormatErrorAction.ThrowError },
        });
        return formatter;
    }
}
