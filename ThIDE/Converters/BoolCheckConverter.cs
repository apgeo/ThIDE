// A yes/no grid cell as a check mark: true → "✓", false → empty. Language-neutral, so a Romanian grid
// no longer shows "True"/"False"; sorting still uses the bound bool.

using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ThIDE.Converters;

public sealed class BoolCheckConverter : IValueConverter
{
    public static readonly BoolCheckConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "✓" : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
