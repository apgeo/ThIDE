// Reactive binding source behind the {l:Loc Key} markup extension. Its string indexer resolves a
// Strings.resx key to the value for the current UI culture; calling Invalidate() on a language
// switch raises INotifyPropertyChanged for the indexer, so every {l:Loc} binding re-reads and
// already-loaded views relocalize live (previously {l:Loc} resolved once at load, leaving panels
// such as Generated Files' Columns/Fit buttons stuck in the old language after a switch).

using System.ComponentModel;

namespace TherionProc.Resources;

public sealed class LocProxy : INotifyPropertyChanged
{
    public static LocProxy Instance { get; } = new();
    private LocProxy() { }

    /// <summary>Localized string for <paramref name="key"/> in the active UI culture.</summary>
    public string this[string key] => Tr.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Re-evaluates every {l:Loc} binding; call after the UI language changes.
    /// An empty property name means "all properties changed", which Avalonia honours for the
    /// indexer binding regardless of how the indexer's change name is spelled.</summary>
    public void Invalidate() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
