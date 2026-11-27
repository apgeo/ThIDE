// Code-behind of the "Derive Survey from 3-D Model" window: view glue only (the view model does the work).

using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class MeshSurveyWindow : Window
{
    public MeshSurveyWindow() : this(new MeshSurveyViewModel()) { }

    public MeshSurveyWindow(MeshSurveyViewModel model)
    {
        InitializeComponent();
        this.ClampOnOpen();
        this.FocusOnOpen("ModelPathBox");
        DataContext = model;
    }

    public MeshSurveyViewModel Model => (MeshSurveyViewModel)DataContext!;

    // A running plan is cancelled when the window goes away.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MeshSurveyViewModel model) model.Cancel();
        base.OnClosing(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
