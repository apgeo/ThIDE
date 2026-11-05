// The Compiler Output text colours are theme resources with at least 4.5:1 (WCAG AA, normal text) on the
// panel in both themes. Plain lines used a fixed #202020, which on the dark theme's black panel is 1.3:1.
// A blank output line (Therion prints them; on stderr they are classified as warnings) shows no glyph.

using Avalonia.Media;
using Avalonia.Styling;
using ThIDE.ViewModels;
using ThIDE.Views.Docking;

namespace ThIDE.Tests;

public sealed class CompilerOutputContrastTests
{
    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static TheoryData<string> Variants => new() { "Light", "Dark" };
    private static ThemeVariant Of(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    [Theory]
    [MemberData(nameof(Variants))]
    public void Every_output_colour_is_readable_on_the_panel(string variant)
    {
        var colors = CompilerOutputPalette.Colors(Of(variant));
        foreach (var tone in Enum.GetValues<OutputTone>()) Assert.True(colors.ContainsKey(CompilerOutputPalette.Key(tone)), $"{tone} has no colour");
        Assert.True(colors.ContainsKey(CompilerOutputPalette.TimeKey));

        foreach (var background in CompilerOutputPalette.Backgrounds(Of(variant)))
            Assert.All(colors, c => Assert.True(Contrast(c.Value, background) >= 4.5,
                $"{c.Key} {c.Value} on {background} ({variant}): {Contrast(c.Value, background):0.00}"));
    }

    [Fact]
    public void Plain_lines_are_no_longer_the_near_black_of_the_report()
    {
        var black = Color.Parse("#000000");
        Assert.True(Contrast(Color.Parse("#202020"), black) < 1.5);   // the reported colour
        var text = CompilerOutputPalette.Colors(ThemeVariant.Dark)[CompilerOutputPalette.Key(OutputTone.Text)];
        Assert.True(Contrast(text, black) >= 7);
    }

    [Fact]
    public void Both_themes_carry_the_same_keys()
    {
        Assert.Equal(CompilerOutputPalette.Colors(ThemeVariant.Light).Keys.OrderBy(k => k),
                     CompilerOutputPalette.Colors(ThemeVariant.Dark).Keys.OrderBy(k => k));
    }

    private static CompilerOutputRow Row(string text, string severity) => new(text, severity, null,
        OutputRowKind.Normal, DateTimeOffset.UnixEpoch, TimeSpan.Zero, TimeSpan.Zero, TimeColumnMode.SinceLast);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_warning_line_is_a_blank_row_without_a_glyph(string text)
    {
        var row = Row(text, "Warning");
        Assert.False(row.ShowWarnIcon);
        Assert.False(row.ShowErrorIcon);
        Assert.False(row.ShowInfoIcon);
        Assert.Equal(OutputTone.Text, row.Tone);
        Assert.Equal(text, row.Text);   // kept verbatim: the Raw tab and Copy All still match the output
    }

    [Fact]
    public void Lines_with_a_message_keep_their_severity()
    {
        var warning = Row("warning -- something odd", "Warning");
        Assert.True(warning.ShowWarnIcon);
        Assert.Equal(OutputTone.Warning, warning.Tone);
        Assert.Equal(OutputTone.Error, Row("error -- broken", "Error").Tone);
        var info = Row("reading source files ... done", "Info");
        Assert.True(info.ShowInfoIcon);
        Assert.Equal(OutputTone.Text, info.Tone);
    }
}
