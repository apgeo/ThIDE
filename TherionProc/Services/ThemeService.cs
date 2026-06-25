// Applies the app theme (#2): the light/dark/system variant that drives all Fluent control
// colors, and the user's optional custom syntax colors pushed into the editor colorizer.
// Re-applies whenever settings change, so the Preferences "Theme" section takes effect live.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Therion.Syntax;
using TherionProc.Editor;

namespace TherionProc.Services;

public interface IThemeService
{
    /// <summary>Applies the current settings' theme + syntax colors (called once at startup).</summary>
    void Apply();
}

public sealed class ThemeService : IThemeService
{
    private readonly IAppSettingsService _settings;

    public ThemeService(IAppSettingsService settings)
    {
        _settings = settings;
        _settings.Changed += (_, _) => OnUi(Apply);
    }

    public void Apply()
    {
        var s = _settings.Current;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = s.ThemeMode switch
            {
                "Light" => ThemeVariant.Light,
                "Dark"  => ThemeVariant.Dark,
                _       => ThemeVariant.Default,
            };

        TherionColorizer.SetCustomColors(s.UseCustomSyntaxColors ? BuildOverrides(s) : null);
    }

    private static Dictionary<TokenClassification, IBrush>? BuildOverrides(AppSettings s)
    {
        var map = new Dictionary<TokenClassification, IBrush>();
        Add(map, TokenClassification.Keyword, s.SyntaxKeywordColor);
        Add(map, TokenClassification.Identifier, s.SyntaxIdentifierColor);
        Add(map, TokenClassification.Number, s.SyntaxNumberColor);
        Add(map, TokenClassification.String, s.SyntaxStringColor);
        Add(map, TokenClassification.Comment, s.SyntaxCommentColor);
        Add(map, TokenClassification.Option, s.SyntaxOptionColor);
        Add(map, TokenClassification.Punctuation, s.SyntaxPunctuationColor);
        return map.Count > 0 ? map : null;
    }

    private static void Add(IDictionary<TokenClassification, IBrush> map, TokenClassification c, string? hex)
    {
        if (TryParseColor(hex, out var color))
            map[c] = new ImmutableSolidColorBrush(color);
    }

    /// <summary>Parses a #RRGGBB (or #AARRGGBB) hex color; tolerant of a missing leading '#'.</summary>
    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var t = hex.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        try { color = Color.Parse(t); return true; }
        catch { return false; }
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
