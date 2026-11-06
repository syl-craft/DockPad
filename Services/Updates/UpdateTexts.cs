using System.Globalization;

namespace DockPad.Services.Updates;

public interface IUpdateTexts
{
    string T(string key);
    string F(string key, params object?[] args);
    CultureInfo Formatting { get; }
    event EventHandler? LanguageChanged;
}

public static class UpdateTexts
{
    private static IUpdateTexts? _texts;
    private static IUpdateTexts Texts => _texts ?? throw new InvalidOperationException("Update texts are not configured");
    public static string T(string key) => Texts.T(key);
    public static string F(string key, params object?[] args) => Texts.F(key, args);
    public static CultureInfo Formatting => Texts.Formatting;
    public static event EventHandler? LanguageChanged;

    public static void Configure(IUpdateTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (_texts != null) _texts.LanguageChanged -= OnLanguageChanged;
        _texts = texts;
        _texts.LanguageChanged += OnLanguageChanged;
        OnLanguageChanged(null, EventArgs.Empty);
    }

    private static void OnLanguageChanged(object? sender, EventArgs e) => LanguageChanged?.Invoke(sender, e);
}
