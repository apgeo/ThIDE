// Multiplies a double by the ConverterParameter ratio. Used to cap the status-bar file-path
// width at a fraction (65%) of the window width (#10).

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TherionProc.Converters;

public sealed class RatioConverter : IValueConverter
{
    public static readonly RatioConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d) return Avalonia.Data.BindingOperations.DoNothing;
        var ratio = parameter switch
        {
            double p => p,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) => r,
            _ => 1.0,
        };
        return d * ratio;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
