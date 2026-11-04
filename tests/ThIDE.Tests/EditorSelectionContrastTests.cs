// Selected text keeps its syntax colours, so each palette colour must stay readable on the selection.
// AvaloniaEdit's default selection (the opaque Fluent accent #0078D7) hid the light theme's numbers.

using Avalonia.Media;
using ThIDE.Editor;

namespace ThIDE.Tests;

public sealed class EditorSelectionContrastTests
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

    [Theory]
    [InlineData(TherionColorizer.Variant.Light)]
    [InlineData(TherionColorizer.Variant.Dark)]
    public void Every_token_colour_is_readable_inside_a_selection(TherionColorizer.Variant variant)
    {
        var selection = ((ISolidColorBrush)TherionColorizer.SelectionBrush(variant)).Color;
        Assert.Equal(255, selection.A);   // opaque: the contrast is what is drawn

        Assert.All(TherionColorizer.Palette(variant), token =>
            Assert.True(Contrast(((ISolidColorBrush)token.Value).Color, selection) >= 3.0,
                $"{token.Key} on the {variant} selection: {Contrast(((ISolidColorBrush)token.Value).Color, selection):0.00}"));
    }

    [Fact]
    public void The_light_selection_is_not_the_accent_that_hid_the_numbers()
    {
        var accent = Color.Parse("#0078D7");
        var number = ((ISolidColorBrush)TherionColorizer.Palette(TherionColorizer.Variant.Light)[Therion.Syntax.TokenClassification.Number]).Color;
        Assert.True(Contrast(number, accent) < 1.5);   // the reported problem: teal on the accent blue
        Assert.NotEqual(accent, ((ISolidColorBrush)TherionColorizer.SelectionBrush(TherionColorizer.Variant.Light)).Color);
    }
}
