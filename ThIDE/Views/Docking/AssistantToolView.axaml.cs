using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class AssistantToolView : UserControl
{
    private bool _autoScrollWired;

    public AssistantToolView()
    {
        InitializeComponent();
        // Keep the newest transcript entry in view while a turn streams in. Wired once — the view
        // can re-attach on dock/float moves, and the content VM is a singleton.
        this.AttachedToVisualTree += (_, _) =>
        {
            if (_autoScrollWired || DataContext is not AssistantToolViewModel { Assistant: { } vm }) return;
            _autoScrollWired = true;
            vm.Items.CollectionChanged += (_, _) => Transcript.ScrollToEnd();
        };
    }

    /// <summary>Enter sends (the box is single-line; Shift+Enter is not a concern).</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is AssistantToolViewModel { Assistant: { } vm } && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
