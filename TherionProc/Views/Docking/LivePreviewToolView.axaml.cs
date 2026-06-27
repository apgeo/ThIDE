using Avalonia.Controls;
using TherionProc.ViewModels.Docking;
using TherionProc.Views;

namespace TherionProc.Views.Docking;

public partial class LivePreviewToolView : UserControl
{
    public LivePreviewToolView()
    {
        InitializeComponent();
        if (this.FindControl<LivePreviewControl>("Sketch") is { } sketch)
            sketch.SegmentActivated += (_, span) =>
            {
                if (DataContext is LivePreviewToolViewModel vm) vm.Preview.Activate(span);
            };
    }
}
