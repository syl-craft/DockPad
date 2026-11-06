using System.Globalization;
using System.Runtime.CompilerServices;
using Mue;

namespace DockPad.Services.UpdateHost;

public sealed class LocUpdateTexts : IUpdateTexts
{
    public string T(string key) => Loc.T(key);
    public string F(string key, params object?[] args) => Loc.F(key, args);
    public CultureInfo Formatting => Loc.Formatting;
    public event EventHandler? LanguageChanged
    {
        add => Loc.LanguageChanged += value;
        remove => Loc.LanguageChanged -= value;
    }

    // Les getters des modèles lisent les textes avant tout démarrage, y compris dans les tests et les outils.
    [ModuleInitializer]
    internal static void Initialize() => UpdateTexts.Configure(new LocUpdateTexts());
}
