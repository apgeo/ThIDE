// Maps a workspace node kind to its Material icon geometry (from Icons.axaml),
// so the tree renders true vector icons rather than Unicode glyphs (#4).

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ThIDE.Converters;

public sealed class KindToIconConverter : IValueConverter
{
    public static readonly KindToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string switch
        {
            "thconfig" => "Icon.Config",
            "th"       => "Icon.Th",
            "th2"      => "Icon.Th2",
            "xvi"      => "Icon.Xvi",
            "image"    => "Icon.Xvi",
            "survey"   => "Icon.Survey",
            "map"      => "Icon.Map",
            "scrap"    => "Icon.Scrap",
            "missing"  => "Icon.Warning",
            "folder"   => "Icon.Folder",
            "file"     => "Icon.File",
            _          => "Icon.File",
        };

        if (Application.Current is { } app &&
            app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is Geometry geometry)
            return geometry;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
