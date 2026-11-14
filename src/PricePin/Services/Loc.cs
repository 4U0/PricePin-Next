using System.Globalization;
using System.Windows;
using PricePin.Core.Settings;

namespace PricePin.Services;

/// <summary>A language the UI is translated into, shown by its own name ("Türkçe").</summary>
public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>
/// UI text. Strings live in <c>Strings/Strings.&lt;code&gt;.xaml</c>; XAML reads them with
/// <c>{DynamicResource Key}</c> so a language switch updates open windows at once, and code
/// reads them with <see cref="T"/> and <see cref="F"/>. English is always loaded underneath
/// the chosen language, so a key a translation hasn't caught up on still shows in English.
/// To add a language: copy Strings.en.xaml, translate it, and add it to <see cref="Languages"/>.
/// </summary>
public static class Loc
{
    public const string Fallback = "en";

    public static IReadOnlyList<LanguageInfo> Languages { get; } =
    [
        new("en", "English"),
        new("tr", "Türkçe"),
    ];

    private static ResourceDictionary? _fallback;
    private static ResourceDictionary? _overlay;

    /// <summary>The language currently showing, e.g. "tr".</summary>
    public static string Current { get; private set; } = Fallback;

    /// <summary>Raised after the language changes, so code-built text can be rebuilt.</summary>
    public static event EventHandler? Changed;

    /// <summary>Resolves a setting ("system", "en", "tr") to a language that exists.</summary>
    public static string Resolve(string? setting)
    {
        var code = string.IsNullOrWhiteSpace(setting) || setting == AppSettings.SystemLanguage
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : setting;
        return Languages.Any(l => l.Code == code) ? code : Fallback;
    }

    public static void Apply(string? setting)
    {
        var resources = Application.Current.Resources;
        var code = Resolve(setting);

        if (_fallback is null)
        {
            _fallback = Load(Fallback);
            resources.MergedDictionaries.Add(_fallback);
        }

        if (_overlay is not null)
        {
            resources.MergedDictionaries.Remove(_overlay);
            _overlay = null;
        }
        if (code != Fallback)
        {
            _overlay = Load(code);
            resources.MergedDictionaries.Add(_overlay); // later dictionaries win
        }

        var changed = code != Current;
        Current = code;
        if (changed)
            Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The text for <paramref name="key"/>, or the key itself if it's missing.</summary>
    public static string T(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    /// <summary>Formats the text for <paramref name="key"/> with <paramref name="args"/> ({0}, {1}, ...).</summary>
    public static string F(string key, params object?[] args)
    {
        var format = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format; // a broken translation must never crash the app
        }
    }

    private static ResourceDictionary Load(string code) =>
        new() { Source = new Uri($"pack://application:,,,/Strings/Strings.{code}.xaml") };
}
