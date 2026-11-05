// Text colours of the Compiler Output panel, per theme. They are theme resources (not fixed brushes on
// the rows), so a theme switch recolours the output already shown: the old fixed near-black for plain
// lines (#202020) was 1.3:1 on the dark theme's black panel. Every colour keeps at least 4.5:1 (WCAG AA
// for normal text) on the panel backgrounds below; the tests check it.

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using ThIDE.ViewModels;

namespace ThIDE.Views.Docking;

internal static class CompilerOutputPalette
{
    /// <summary>The resource key the view's styles and the Raw tab look a tone up by.</summary>
    public static string Key(OutputTone tone) => "CompilerOutput." + tone;

    /// <summary>The time column's resource key.</summary>
    public const string TimeKey = "CompilerOutput.Time";

    /// <summary>
    /// What the output is drawn on: the window background (Fluent: white / black) and, as a margin, the
    /// lifted surface a docked tool panel can have.
    /// </summary>
    public static IReadOnlyList<Color> Backgrounds(ThemeVariant variant) => variant == ThemeVariant.Dark
        ? new[] { Color.Parse("#000000"), Color.Parse("#1F1F1F") }
        : new[] { Color.Parse("#FFFFFF"), Color.Parse("#F3F3F3") };

    /// <summary>The colour per tone, plus the time column under <see cref="TimeKey"/>.</summary>
    public static IReadOnlyDictionary<string, Color> Colors(ThemeVariant variant) => variant == ThemeVariant.Dark
        ? new Dictionary<string, Color>
        {
            [Key(OutputTone.Text)] = Color.Parse("#D4D4D4"),
            [Key(OutputTone.Command)] = Color.Parse("#B0B0B0"),
            [Key(OutputTone.Warning)] = Color.Parse("#E0B23A"),
            [Key(OutputTone.Error)] = Color.Parse("#F47174"),
            [Key(OutputTone.Success)] = Color.Parse("#6CC070"),
            [TimeKey] = Color.Parse("#A0A0A0"),
        }
        : new Dictionary<string, Color>
        {
            [Key(OutputTone.Text)] = Color.Parse("#202020"),
            [Key(OutputTone.Command)] = Color.Parse("#555555"),
            [Key(OutputTone.Warning)] = Color.Parse("#7A5C00"),
            [Key(OutputTone.Error)] = Color.Parse("#C62828"),
            [Key(OutputTone.Success)] = Color.Parse("#2E7D32"),
            [TimeKey] = Color.Parse("#6B6B6B"),
        };

    /// <summary>Adds the light and dark brushes to <paramref name="resources"/> as theme dictionaries.</summary>
    public static void AddTo(IResourceDictionary resources)
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var dict = new ResourceDictionary();
            foreach (var (key, color) in Colors(variant)) dict[key] = new ImmutableSolidColorBrush(color);
            resources.ThemeDictionaries[variant] = dict;
        }
    }
}
