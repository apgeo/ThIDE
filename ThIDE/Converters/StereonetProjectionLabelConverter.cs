// Localized display label for the stereonet projection combo: EqualAngle → "Wulff (equal-angle)",
// EqualArea → "Schmidt (equal-area)". Purely cosmetic — the bound SelectedItem stays the enum.

using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Therion.Structural;

namespace ThIDE.Converters;

public sealed class StereonetProjectionLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StereonetProjection p
            ? ThIDE.Resources.Tr.Get(p == StereonetProjection.EqualAngle ? "Struct_ProjWulff" : "Struct_ProjSchmidt")
            : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
