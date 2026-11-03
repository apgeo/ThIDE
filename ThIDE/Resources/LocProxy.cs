// Reactive binding source behind the {l:Loc Key} markup extension. Its string indexer resolves a
// Strings.resx key to the value for the current UI culture; calling Invalidate() on a language
// switch raises INotifyPropertyChanged for the indexer, so every {l:Loc} binding re-reads and
// already-loaded views relocalize live (previously {l:Loc} resolved once at load, leaving panels
// such as Generated Files' Columns/Fit buttons stuck in the old language after a switch).

using System.ComponentModel;

namespace ThIDE.Resources;

public sealed class LocProxy : INotifyPropertyChanged
{
    public static LocProxy Instance { get; } = new();
    private LocProxy() { }

    /// <summary>Localized string for <paramref name="key"/> in the active UI culture.</summary>
    public string this[string key] => Tr.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    // Avalonia's reflection indexer binding node re-reads only when PropertyName names a declared
    // indexer property of the source type (GetDeclaredProperty(name) with index parameters), i.e. the
    // CLR indexer name "Item". WPF's "Item[]" and an empty "all properties" name are both ignored by
    // Avalonia 12, which left every {l:Loc} binding (menu headers, status bar, panel toolbars) on the
    // language it first read.
    internal static readonly PropertyChangedEventArgs IndexerChanged = new("Item");

    /// <summary>Re-evaluates every {l:Loc} binding; call after the UI language changes.</summary>
    public void Invalidate() => PropertyChanged?.Invoke(this, IndexerChanged);
}
