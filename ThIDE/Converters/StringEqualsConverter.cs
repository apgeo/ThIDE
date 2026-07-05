// True when the bound value's string form equals the ConverterParameter (case-insensitive).
// Used to render mutually-exclusive choice buttons (e.g. the 3D viewer's Color-by) as
// pushed/unpushed by comparing the current mode to each button's parameter.

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ThIDE.Converters;

public sealed class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    // One-way: the button's Command updates the source; IsChecked is recomputed from it.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}
