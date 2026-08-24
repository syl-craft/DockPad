using System.Windows.Data;
using System.Windows.Markup;

namespace DockPad.Services.Localization;

/// <summary>
/// Extension de balisage <c>{loc:T Cle}</c> : le texte traduit d'une clé, qui suit les changements
/// de langue sans redémarrage.
/// </summary>
/// <remarks>
/// <para>
/// Elle ne fabrique rien d'autre qu'une liaison vers l'indexeur de <see cref="Loc.Instance"/> :
/// <c>{loc:T Settings_Title}</c> équivaut à
/// <c>{Binding Path=[Settings_Title], Source={x:Static loc:Loc.Instance}}</c>, en dix fois moins de
/// caractères. Tout le mécanisme de bascule est dans <see cref="Loc.SetCulture"/>, qui invalide les
/// liaisons d'indexeur ; il n'y a donc <b>aucun état ici</b>, et rien à désabonner quand une fenêtre
/// se ferme.
/// </para>
/// <para>
/// C'est aussi le seul fichier de localisation qui référence WPF, ce qui garde <see cref="Loc"/>
/// testable sans instance <c>Application</c>.
/// </para>
/// <para>
/// Le suffixe <c>Extension</c> est retiré par XAML : la classe <c>TExtension</c> s'écrit
/// <c>{loc:T …}</c>.
/// </para>
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    /// <summary>Clé de ressource, au format <c>Zone_Element</c>.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // ProvideValue de la Binding, et non la Binding elle-même : c'est ce qui fait fonctionner
        // l'extension aussi bien sur une propriété de dépendance que dans un Setter de style ou un
        // template, où une Binding brute serait rendue telle quelle.
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>
/// Feedback passager sur un bouton — « Copié ✓ » — sans perdre sa traduction.
/// </summary>
/// <remarks>
/// Assigner <c>Content</c> pose une <b>valeur locale</b>, qui remplace la liaison <c>{loc:T}</c> :
/// remettre ensuite l'ancien texte rendrait le bouton sourd aux changements de langue pour le reste
/// de la session. On mémorise donc la liaison et on la repose, plutôt que de recopier une chaîne.
/// </remarks>
public static class ButtonFlash
{
    public static void Flash(System.Windows.Controls.Button button, string message,
                             TimeSpan duration)
    {
        var binding = System.Windows.Data.BindingOperations.GetBinding(
            button, System.Windows.Controls.ContentControl.ContentProperty);
        var original = button.Content;

        button.Content = message;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (binding is not null)
                System.Windows.Data.BindingOperations.SetBinding(
                    button, System.Windows.Controls.ContentControl.ContentProperty, binding);
            else
                button.Content = original;
        };
        timer.Start();
    }
}
