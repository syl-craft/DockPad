using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Thème clair / sombre : la décision, et la parité des deux dictionnaires.
/// </summary>
/// <remarks>
/// Rien ici ne monte WPF. La décision « quel thème appliquer » est une fonction pure, et la
/// parité se vérifie en lisant les deux fichiers — comme celle des deux fichiers de traduction.
/// </remarks>
public class ThemeTests
{
    // ---------------------------------------------------------------- la décision

    [Theory]
    [InlineData("Dark", false, true)]    // choix explicite : le système ne décide plus
    [InlineData("Dark", true, true)]
    [InlineData("Light", true, false)]
    [InlineData("Light", false, false)]
    public void IsDark_UnChoixExplicite_LEmporteSurLeSysteme(string setting, bool systemIsDark, bool expected)
        => Assert.Equal(expected, ThemeService.IsDark(setting, systemIsDark));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsDark_ReglageVide_SuitLeSysteme(bool systemIsDark)
        => Assert.Equal(systemIsDark, ThemeService.IsDark("", systemIsDark));

    /// <summary>
    /// Même convention que <c>Language</c> et <c>TriggerFirst</c> : le vide veut dire « laisse le
    /// système décider ». Une valeur inconnue — un réglage écrit par une version future, puis
    /// revenue en arrière — doit se comporter comme le vide, pas planter ni figer un thème.
    /// </summary>
    [Theory]
    [InlineData("Sombre")]
    [InlineData("auto")]
    [InlineData("   ")]
    public void IsDark_ValeurInconnue_SeComporteCommeAutomatique(string setting)
    {
        Assert.True(ThemeService.IsDark(setting, systemIsDark: true));
        Assert.False(ThemeService.IsDark(setting, systemIsDark: false));
    }

    /// <summary>La casse ne doit pas décider du thème : le réglage vient d'un fichier de registre.</summary>
    [Theory]
    [InlineData("dark")]
    [InlineData("DARK")]
    public void IsDark_InsensibleALaCasse(string setting)
        => Assert.True(ThemeService.IsDark(setting, systemIsDark: false));

    /// <summary>
    /// Un choix explicite doit rester insensible à un changement de thème de Windows.
    /// </summary>
    /// <remarks>
    /// C'est ce qui décide si l'écoute de <c>UserPreferenceChanged</c> doit réagir : sans ce
    /// filtre, basculer Windows en sombre écraserait le choix « Clair » de l'utilisateur.
    /// </remarks>
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("valeur inconnue", true)]
    [InlineData("Light", false)]
    [InlineData("Dark", false)]
    [InlineData("dark", false)]
    public void FollowsSystem_VraiSeulementSansChoixExplicite(string setting, bool expected)
        => Assert.Equal(expected, ThemeService.FollowsSystem(setting));

    // ---------------------------------------------------------------- parité des dictionnaires

    /// <summary>
    /// Les deux dictionnaires portent exactement les mêmes clés.
    /// </summary>
    /// <remarks>
    /// Sans ce garde, une clé ajoutée d'un seul côté ne se verrait que sur l'écran concerné, dans
    /// le thème concerné — et <c>DynamicResource</c> ne lève pas : il rend simplement une valeur
    /// nulle, donc un fond transparent ou un texte invisible.
    /// </remarks>
    [Fact]
    public void LesDeuxThemes_PortentLesMemesCles()
    {
        var light = Keys("Light.xaml");
        var dark = Keys("Dark.xaml");

        Assert.Empty(light.Except(dark));
        Assert.Empty(dark.Except(light));
    }

    [Fact]
    public void LesDeuxThemes_NeDefinissentAucuneCleEnDouble()
    {
        foreach (var file in new[] { "Light.xaml", "Dark.xaml" })
        {
            var all = Brushes(file).Select(b => b.Attribute(X + "Key")!.Value).ToList();

            Assert.Equal(all.Count, all.Distinct().Count());
        }
    }

    /// <summary>
    /// Une brosse sans couleur, ou dont la couleur n'est pas lisible, se traduirait par du
    /// transparent à l'écran — silencieusement.
    /// </summary>
    [Fact]
    public void LesDeuxThemes_NOntQueDesCouleursLisibles()
    {
        foreach (var file in new[] { "Light.xaml", "Dark.xaml" })
            foreach (var brush in Brushes(file))
            {
                var key = brush.Attribute(X + "Key")!.Value;
                var color = brush.Attribute("Color")?.Value;

                Assert.False(string.IsNullOrWhiteSpace(color), $"{file} : {key} n'a pas de couleur");
                Assert.Matches("^#[0-9A-Fa-f]{6}$|^#[0-9A-Fa-f]{8}$", color!);
            }
    }

    /// <summary>
    /// Le thème clair reste ce qu'il a toujours été : c'est ce qui autorise à dire qu'ajouter un
    /// thème sombre ne change rien pour qui n'en veut pas. Quelques ancres suffisent — la
    /// vérification complète est la comparaison pixel des fenêtres.
    /// </summary>
    [Theory]
    [InlineData("Brush.Accent", "#0078D4")]
    [InlineData("Brush.Surface", "#F3F3F3")]
    [InlineData("Brush.SurfaceCard", "#FFFFFF")]
    [InlineData("Brush.Text", "#1A1A1A")]
    public void ThemeClair_GardeSesValeursDOrigine(string key, string expected)
        => Assert.Equal(expected, ColorOf("Light.xaml", key));

    /// <summary>Un thème sombre dont le fond serait clair n'en serait pas un.</summary>
    [Fact]
    public void ThemeSombre_ALeContrasteInverse()
    {
        Assert.True(Luminance(ColorOf("Dark.xaml", "Brush.Surface")) < 0.3);
        Assert.True(Luminance(ColorOf("Dark.xaml", "Brush.SurfaceCard")) < 0.3);
        Assert.True(Luminance(ColorOf("Dark.xaml", "Brush.Text")) > 0.7);
    }

    // ---------------------------------------------------------------- lecture des fichiers

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static IEnumerable<XElement> Brushes(string file) =>
        XDocument.Load(Path.Combine(RepoRoot(), "Themes", file))
                 .Root!.Elements()
                 .Where(e => e.Name.LocalName == "SolidColorBrush");

    private static HashSet<string> Keys(string file) =>
        Brushes(file).Select(b => b.Attribute(X + "Key")!.Value).ToHashSet();

    private static string ColorOf(string file, string key) =>
        Brushes(file).First(b => b.Attribute(X + "Key")!.Value == key).Attribute("Color")!.Value;

    private static double Luminance(string hex)
    {
        var rgb = hex[^6..];
        double Channel(int i) => Convert.ToInt32(rgb.Substring(i * 2, 2), 16) / 255.0;

        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
}
