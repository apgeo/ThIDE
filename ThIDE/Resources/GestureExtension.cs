// {l:Gesture CommandId} XAML markup extension — yields the KeyGesture currently bound to a shell
// command, for MenuItem.InputGesture. Binds to the reactive GestureProxy so a remap in
// Settings ▸ Keyboard immediately updates the chord shown next to the menu item. Unbound commands
// resolve to null, which renders no label. Returns a single Binding (same shape as LocExtension).

using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace ThIDE.Resources;

public sealed class GestureExtension : MarkupExtension
{
    public GestureExtension() { }
    public GestureExtension(string commandId) => CommandId = commandId;

    public string CommandId { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Source = GestureProxy.Instance,
        Path = $"[{CommandId}]",
        Mode = BindingMode.OneWay,
    };
}
