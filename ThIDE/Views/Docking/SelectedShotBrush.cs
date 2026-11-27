// The background of a shot's row in the Blender panel's Storyboard tab: tinted for the shot the
// settings edit. Immutable brushes, shared by every row. Beside it, the style of a side passage
// the film leaves out in the branch table of a tour shot.

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ThIDE.Views.Docking;

public sealed class SelectedShotBrush : IValueConverter
{
    public static readonly SelectedShotBrush Instance = new();

    private static readonly IBrush Selected = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0x4A, 0x90, 0xD9));
    private static readonly IBrush None = new ImmutableSolidColorBrush(Colors.Transparent);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Selected : None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    /// <summary>A side passage the film does not visit is written in italics in the branch table.</summary>
    public static readonly IValueConverter LeftOut = new FuncValueConverter<bool, FontStyle>(visited => visited ? FontStyle.Normal : FontStyle.Italic);
}
