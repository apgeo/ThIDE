// Ctrl+Tab switcher (#2). A small MRU overlay listing the things the central document well can show —
// the open editor documents AND any non-file tool panels docked there (Object Browser, Structural
// Geology, …). Shown while Ctrl is held: each Tab advances the selection, Ctrl-release activates it.
// Driven from the window (focus-agnostic) by MainWindowViewModel; this type only holds overlay state.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Core;

namespace ThIDE.ViewModels;

/// <summary>One row in the Ctrl+Tab switcher overlay (a file document or a central tool panel).</summary>
public sealed partial class DocumentSwitcherItem : ObservableObject
{
    public DocumentSwitcherItem(IDockable dockable, string title, string detail, string iconKey)
    {
        Dockable = dockable;
        Title = title;
        Detail = detail;
        IconKey = iconKey;
    }

    /// <summary>The dockable this row activates when picked.</summary>
    public IDockable Dockable { get; }
    public string Title { get; }
    public string Detail { get; }
    /// <summary>DynamicResource geometry key for the row glyph (file vs. panel).</summary>
    public string IconKey { get; }

    /// <summary>Highlighted row (the one Ctrl-release will activate).</summary>
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Visibility + items for the Ctrl+Tab switcher overlay.</summary>
public sealed partial class DocumentSwitcherViewModel : ObservableObject
{
    public ObservableCollection<DocumentSwitcherItem> Items { get; } = new();

    /// <summary>Whether the overlay is currently shown.</summary>
    [ObservableProperty] private bool _isOpen;
}
