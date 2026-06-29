using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class OutlineToolView : UserControl
{
    public OutlineToolView() => InitializeComponent();

    // #1: a single click just folds/unfolds a parent node (the chevron is left to the tree itself);
    // navigation is reserved for double-click so the tree is comfortable to browse.
    private void OnTreeTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ToggleButton>() is not null) return; // expander chevron
        if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>() is { ItemCount: > 0 } item)
            item.IsExpanded = !item.IsExpanded;
    }

    // #1: double click navigates the editor to the node's source line.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OutlineToolViewModel vm &&
            (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>()?.DataContext is OutlineNode node)
            vm.Outline.NavigateToNode(node);
    }
}
