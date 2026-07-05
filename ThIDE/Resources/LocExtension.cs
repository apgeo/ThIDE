// {l:Loc Some_Key} XAML markup extension — binds a Strings.resx key to its localized value via the
// reactive LocProxy source (#2). Because it returns a binding (not a one-shot string), the value
// re-reads whenever the UI language changes, so already-loaded content relocalizes live in addition
// to content that is created fresh when shown (context menus, the Preferences window).

using Avalonia.Data;
using Avalonia.Markup.Xaml;
using System;

namespace ThIDE.Resources;

public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Source = LocProxy.Instance,
        Path = $"[{Key}]",
        Mode = BindingMode.OneWay,
    };
}
