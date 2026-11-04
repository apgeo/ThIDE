using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class OutlineToolView : UserControl
{
    public OutlineToolView()
    {
        InitializeComponent();
        // handledEventsToo: the tree item marks the release handled once it has selected and focused.
        this.FindControl<TreeView>("Tree")?.AddHandler(PointerReleasedEvent, OnTreePointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // A single click shows the node's line in the editor and leaves focus in the tree; folding stays
    // on the expander chevron (and Left/Right), so browsing the outline never needs a double-click.
    private void OnTreeTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ToggleButton>() is not null) return; // expander chevron
        if (DataContext is OutlineToolViewModel vm &&
            (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>()?.DataContext is OutlineNode node)
            vm.Outline.RevealNode(node);
    }

    // #1: double click navigates the editor to the node's source line and moves focus there. The jump
    // waits for the button release: the tree focuses the clicked item on release, which would
    // otherwise take the keyboard straight back from the editor.
    private OutlineNode? _navigateOnRelease;

    // Marked handled so the item does not also fold or unfold, which kept the keyboard in the tree.
    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not OutlineNode node) return;
        _navigateOnRelease = node;
        e.Handled = true;
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_navigateOnRelease is not { } node) return;
        _navigateOnRelease = null;
        if (DataContext is OutlineToolViewModel vm) vm.Outline.NavigateToNode(node);
    }
}
