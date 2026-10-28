using Avalonia.Controls;

namespace ThIDE.Views.Docking;

/// <summary>The Mainline Preview dock panel: <see cref="LivePreviewView"/> plus the panel's window controls.</summary>
public partial class LivePreviewToolView : UserControl
{
    public LivePreviewToolView()
    {
        InitializeComponent();
        // The window controls sit inside the preview view (whose DataContext is the preview), but
        // act on this panel: hand them the panel's own view-model.
        var controls = this.FindControl<PanelWindowControls>("WindowControls");
        DataContextChanged += (_, _) => { if (controls is not null) controls.DataContext = DataContext; };
    }
}
