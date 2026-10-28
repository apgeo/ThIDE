using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ThIDE.ViewModels;

namespace ThIDE.Views;

/// <summary>
/// The Mainline Preview (toolbar, sketch, legend) over a <see cref="LivePreviewViewModel"/>. Hosted
/// by the dock panel and, in standalone mode, by dialogs that pick stations from it.
/// </summary>
public partial class LivePreviewView : UserControl
{
    /// <summary>Extra content at the right end of the toolbar (the dock panel's window controls).</summary>
    public static readonly StyledProperty<object?> ToolbarExtraProperty =
        AvaloniaProperty.Register<LivePreviewView, object?>(nameof(ToolbarExtra));

    public object? ToolbarExtra
    {
        get => GetValue(ToolbarExtraProperty);
        set => SetValue(ToolbarExtraProperty, value);
    }

    /// <summary>Hides the toolbar for a compact embedded preview.</summary>
    public static readonly StyledProperty<bool> IsToolbarVisibleProperty =
        AvaloniaProperty.Register<LivePreviewView, bool>(nameof(IsToolbarVisible), true);

    public bool IsToolbarVisible
    {
        get => GetValue(IsToolbarVisibleProperty);
        set => SetValue(IsToolbarVisibleProperty, value);
    }

    private readonly LivePreviewControl? _sketch;

    public LivePreviewView()
    {
        InitializeComponent();
        _sketch = this.FindControl<LivePreviewControl>("Sketch");
        if (_sketch is not null)
        {
            _sketch.SegmentActivated += (_, span) => (DataContext as LivePreviewViewModel)?.Activate(span);
            _sketch.StationActivated += (_, point) => (DataContext as LivePreviewViewModel)?.ActivateStation(point);
            // Clicking a structural-plane line selects that plane in the Structural Geology grid.
            _sketch.PlaneActivated += (_, line) => (DataContext as LivePreviewViewModel)?.ActivatePlane(line);
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
