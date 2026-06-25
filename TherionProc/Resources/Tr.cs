// Lightweight access to the Strings.resx / Strings.ro.resx resources from places that
// don't have an injected IStringLocalizer<Strings> — code-behind, dock-tool titles, and
// the {l:Loc Key} XAML markup extension (#2). Resolution always follows the current
// UI culture, which LanguageService keeps in sync when the user switches languages.

using System.Globalization;
using System.Resources;

namespace TherionProc.Resources;

public static class Tr
{
    private static readonly ResourceManager Rm =
        new("TherionProc.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Localized string for <paramref name="key"/>, or the key itself if missing.</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        try { return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key; }
        catch { return key; }
    }
}
