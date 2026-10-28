using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class NewFileWindow : Window
{
    public NewFileWindow() => InitializeComponent();

    public NewFileWindow(NewFileViewModel model) : this() => DataContext = model;

    private NewFileViewModel Model => (NewFileViewModel)DataContext!;

    private async void OnCreate(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await Model.CreateAsync() is { } problem) Model.Message = problem;
            else Close();
        }
        catch (Exception ex) { Model.Message = ex.Message; }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = ThIDE.Resources.Tr.Get("NewFile_Folder") });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) Model.Folder = path;
    }
}
