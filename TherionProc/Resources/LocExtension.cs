// {l:Loc Some_Key} XAML markup extension — resolves a Strings.resx key to its localized
// value at load time (#2). Used for content that is created fresh when shown (context
// menus, the Preferences window) so it reflects the active UI language.

using Avalonia.Markup.Xaml;
using System;

namespace TherionProc.Resources;

public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Tr.Get(Key);
}
