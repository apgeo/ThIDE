// {l:LocOption Opt_GroupBy_} — shows the bound option id (a combo item such as a Group-by or Look-in
// choice) in the UI language, like LocKeyConverter, and shows it again after a language switch: the
// LocProxy binding beside the id re-runs the converter when the language changes, where a plain
// converter binding kept the language the item was first drawn in (the selected Group-by read "None").

using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using ThIDE.Converters;

namespace ThIDE.Resources;

public sealed class LocOptionExtension : MarkupExtension
{
    public LocOptionExtension() { }
    public LocOptionExtension(string prefix) => Prefix = prefix;

    /// <summary>The resx key prefix the option id is appended to, e.g. "Opt_GroupBy_".</summary>
    public string Prefix { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Bindings =
        {
            new Binding(),   // the option id (the item), or a LocOptionItem that names itself
            new Binding { Source = LocProxy.Instance, Path = $"[{Prefix}]", Mode = BindingMode.OneWay },   // the language
        },
        Converter = LocKeyConverter.Instance,
        ConverterParameter = Prefix,
        Mode = BindingMode.OneWay,
    };
}
