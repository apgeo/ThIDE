// Lightweight access to the Strings.resx / Strings.ro.resx resources from places that
// don't have an injected IStringLocalizer<Strings> — code-behind, dock-tool titles, and
// the {l:Loc Key} XAML markup extension (#2). Resolution follows Tr.Culture, the one UI
// language that LanguageService sets when the user switches languages.

using System;
using System.Globalization;
using System.Resources;

namespace ThIDE.Resources;

public static class Tr
{
    private static readonly ResourceManager Rm =
        new("ThIDE.Resources.Strings", typeof(Strings).Assembly);

    internal static ResourceManager Manager => Rm;

    private static volatile CultureInfo? _culture;

    /// <summary>
    /// The UI language every lookup uses. One process-wide value on purpose: the ambient
    /// <see cref="CultureInfo.CurrentUICulture"/> flows with the ExecutionContext, so a binding or
    /// continuation running on a flow captured before a language switch would read the old language.
    /// Until <see cref="ThIDE.Services.LanguageService"/> sets it, the ambient UI culture is used (design time).
    /// </summary>
    public static CultureInfo Culture
    {
        get => _culture ?? CultureInfo.CurrentUICulture;
        internal set => _culture = value;
    }

    /// <summary>Localized string for <paramref name="key"/>, or the key itself if missing.</summary>
    public static string Get(string key) => Find(key) ?? key;

    /// <summary>
    /// <paramref name="key"/><c>_One</c>, <c>_Few</c> or <c>_Many</c>, formatted with <paramref name="n"/>:
    /// "1 file", "5 files", and in Romanian "1 fișier", "5 fișiere", "20 de fișiere". The forms follow
    /// the Romanian rule (few = 0 and 2–19 after the hundreds); English gives _Few the _Many text.
    /// </summary>
    public static string Plural(string key, int n)
    {
        int tail = Math.Abs(n) % 100;
        var form = n == 1 ? "_One" : n == 0 || tail is >= 1 and <= 19 ? "_Few" : "_Many";
        return string.Format(Culture, Get(key + form), n);
    }

    /// <summary>Localized string for <paramref name="key"/>, or null if there is none.</summary>
    public static string? Find(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        try { return Rm.GetString(key, Culture); }
        catch { return null; }
    }
}
