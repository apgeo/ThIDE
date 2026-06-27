// Ctrl+Tab document switcher (#2). A small MRU overlay listing the open editor documents,
// shown while Ctrl is held: each Tab advances the selection, Ctrl-release activates it. Driven
// from the window (focus-agnostic) by MainWindowViewModel; this type only holds the overlay state.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TherionProc.ViewModels.Docking;

namespace TherionProc.ViewModels;

/// <summary>One row in the Ctrl+Tab switcher overlay.</summary>
public sealed partial class DocumentSwitcherItem : ObservableObject
{
    public DocumentSwitcherItem(FileDocumentViewModel document, string title, string detail)
    {
        Document = document;
        Title = title;
        Detail = detail;
    }

    public FileDocumentViewModel Document { get; }
    public string Title { get; }
    public string Detail { get; }

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
