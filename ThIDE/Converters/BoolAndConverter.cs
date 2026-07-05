// True only when every bound boolean is true. Used to gate UI on a conjunction of flags
// (e.g. show the live-preview legend overlay only when it's enabled AND there's >1 group).

using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ThIDE.Converters;

public sealed class BoolAndConverter : IMultiValueConverter
{
    public static readonly BoolAndConverter Instance = new();

    public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var v in values)
            if (v is not true) return false;
        return true;
    }
}
