using Avalonia.Controls;
using Avalonia.Interactivity;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class GeneratedFilesToolView : UserControl
{
    public GeneratedFilesToolView() => InitializeComponent();

    private void OnArtifactDoubleTapped(object? sender, RoutedEventArgs e) => OpenSelected();
    private void OnOpenArtifact(object? sender, RoutedEventArgs e) => OpenSelected();

    private void OnRevealArtifact(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GeneratedFilesToolViewModel vm && vm.Build.SelectedArtifact is { } row)
            vm.Build.RevealArtifactCommand.Execute(row);
    }

    private void OpenSelected()
    {
        if (DataContext is GeneratedFilesToolViewModel vm && vm.Build.SelectedArtifact is { } row)
            vm.Build.OpenArtifactCommand.Execute(row);
    }
}
