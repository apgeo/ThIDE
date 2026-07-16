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

    /// <summary>Enter sends; Up/Down at the box's start/end recall past messages.</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AssistantToolViewModel { Assistant: { } vm }) return;

        switch (e.Key)
        {
            case Key.Enter when vm.SendCommand.CanExecute(null):
                vm.SendCommand.Execute(null);
                e.Handled = true;
                break;

            // Recall only from the edge, so a caret mid-text still moves normally.
            case Key.Up when sender is TextBox { CaretIndex: 0 } up:
                if (vm.RecallPreviousInput()) { up.CaretIndex = vm.Input?.Length ?? 0; e.Handled = true; }
                break;

            case Key.Down when sender is TextBox down && down.CaretIndex >= (down.Text?.Length ?? 0):
                if (vm.RecallNextInput()) { down.CaretIndex = vm.Input?.Length ?? 0; e.Handled = true; }
                break;
        }
    }
}
