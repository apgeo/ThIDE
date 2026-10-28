using Avalonia.Controls;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class PreviewWindow : Window
{
    public PreviewWindow() => InitializeComponent();

    public PreviewWindow(PreviewWindowViewModel model) : this()
    {
        DataContext = model;
        Closed += (_, _) => model.Detach();
    }

    /// <summary>A standalone preview, maximised out of a small embedded one.</summary>
    public static void ShowPreview(Window owner, LivePreviewViewModel preview, string title) =>
        new PreviewWindow(PreviewWindowViewModel.ForPreview(preview, title)).Show(owner);

    /// <summary>The whole workspace with the survey being linked attached at the chosen stations.</summary>
    public static void ShowLink(Window owner, LinkSurveyViewModel link) =>
        new PreviewWindow(PreviewWindowViewModel.ForLink(link)).Show(owner);
}
