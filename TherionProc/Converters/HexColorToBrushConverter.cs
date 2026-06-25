// Converts a #RRGGBB hex string to a brush for the Theme section's color swatches (#2).

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TherionProc.Services;

namespace TherionProc.Converters;

public sealed class HexColorToBrushConverter : IValueConverter
{
    public static readonly HexColorToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ThemeService.TryParseColor(value as string, out var c)
            ? new SolidColorBrush(c)
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
