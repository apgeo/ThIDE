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

    // The .NET convention for "the indexer's values changed" (WPF's Binding.IndexerName). Avalonia's
    // reflection indexer binding node re-reads on this exact name; an empty/"all" property name is
    // NOT reliably honoured for indexer paths (Avalonia 12), which left already-loaded {l:Loc} panel
    // content stuck in the previous language after a switch even though the dock titles (updated
    // imperatively on LanguageChanged) did change.
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");
    private static readonly PropertyChangedEventArgs AllChanged = new(string.Empty);

    /// <summary>Re-evaluates every {l:Loc} binding; call after the UI language changes.</summary>
    public void Invalidate()
    {
        var handler = PropertyChanged;
        if (handler is null) return;
        handler(this, IndexerChanged);   // the signal Avalonia's indexer binding actually listens for
        handler(this, AllChanged);       // belt-and-suspenders for any plain-property listeners
    }
}
