// {l:Tip Some_Key} XAML markup extension — like {l:Loc} but appends the action's current keyboard
// shortcut as " (gesture)" when it has one. It binds to the reactive TipProxy (which folds together
// the UI language and the keybindings), so the tooltip relocalizes on a language switch AND updates
// the shortcut the moment it is remapped. Toolbar buttons with no shortcut show just the localized
// text. Returns a single Binding (same shape as LocExtension) so binding application is identical.

using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace ThIDE.Resources;

public sealed class TipExtension : MarkupExtension
{
    public TipExtension() { }
    public TipExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Source = TipProxy.Instance,
        Path = $"[{Key}]",
        Mode = BindingMode.OneWay,
    };
}
