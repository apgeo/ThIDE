using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class NewTripWindow : Window
{
    public NewTripWindow() => InitializeComponent();

    public NewTripWindow(NewTripViewModel model) : this() => DataContext = model;

    private NewTripViewModel Model => (NewTripViewModel)DataContext!;

    private async void OnCreate(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await Model.CreateAsync() is { } problem) Model.Warnings = [new WarningRow(problem, true)];
            else Close();
        }
        catch (Exception ex) { Model.Warnings = [new WarningRow(ex.Message, true)]; }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
