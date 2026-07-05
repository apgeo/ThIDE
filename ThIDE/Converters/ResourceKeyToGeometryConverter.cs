// Resolves a resource-key string (e.g. "Icon.Cube") to its StreamGeometry from the merged
// application resources, so a data-bound icon key can drive a PathIcon (#4).

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ThIDE.Converters;

public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public static readonly ResourceKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is null) return null;
        return Application.Current.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var res)
            ? res as Geometry
            : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
