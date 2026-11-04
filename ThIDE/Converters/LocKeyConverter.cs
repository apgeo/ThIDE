// Shows an option id (a combo item such as a Group-by or Look-in choice) in the UI language: the
// resx key is the ConverterParameter prefix plus the id, e.g. "Opt_GroupBy_" + "None". The bound
// SelectedItem keeps the id; only the displayed text changes. An id without a key is shown as is.

using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ThIDE.Resources;

namespace ThIDE.Converters;

public sealed class LocKeyConverter : IValueConverter, IMultiValueConverter
{
    public static readonly LocKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Label(parameter as string, value?.ToString());

    /// <summary>The multi-binding form ({l:LocOption}): the id first, then bindings that only signal a language switch.</summary>
    public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        Label(parameter as string, values.Count > 0 ? values[0]?.ToString() : null);

    /// <summary>The text for option <paramref name="id"/> under key prefix <paramref name="prefix"/>.</summary>
    public static string Label(string? prefix, string? id) =>
        string.IsNullOrEmpty(id) ? string.Empty : Tr.Find((prefix ?? string.Empty) + id) ?? id;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
