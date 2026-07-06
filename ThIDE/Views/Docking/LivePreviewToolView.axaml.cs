using Avalonia.Controls;
using Avalonia.Input;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Views.Docking;

public partial class LivePreviewToolView : UserControl
{
    private readonly LivePreviewControl? _sketch;

    public LivePreviewToolView()
    {
        InitializeComponent();
        _sketch = this.FindControl<LivePreviewControl>("Sketch");
        if (_sketch is not null)
        {
            _sketch.SegmentActivated += (_, span) =>
            {
                if (DataContext is LivePreviewToolViewModel vm) vm.Preview.Activate(span);
            };
            // Clicking a structural-plane line selects that plane in the Structural Geology grid.
            _sketch.PlaneActivated += (_, line) =>
            {
                if (DataContext is LivePreviewToolViewModel vm) vm.Preview.ActivatePlane(line);
            };
        }

        // Hovering a legend row frames that survey/file/component in the sketch (zooming out to it).
        // Handlers live on the scroller so the always-on scrollbar / right margin don't clear the
        // highlight as the pointer crosses them (only leaving the whole list does).
        if (this.FindControl<ScrollViewer>("GroupScroller") is { } scroller)
        {
            scroller.PointerMoved += OnGroupPointerMoved;
            scroller.PointerExited += OnGroupPointerExited;
        }
    }

    private void OnGroupPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_sketch is null) return;
        if ((e.Source as Control)?.DataContext is GroupVisibility g && g.HasBounds)
            _sketch.HighlightGroup(g.Key, g.Dimension, g.Info, g.MinX, g.MinY, g.MaxX, g.MaxY);
    }

    private void OnGroupPointerExited(object? sender, PointerEventArgs e)
        => _sketch?.ClearHighlight();
}
