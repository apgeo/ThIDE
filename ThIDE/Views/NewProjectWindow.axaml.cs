using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class NewProjectWindow : Window
{
    public NewProjectWindow() => InitializeComponent();

    public NewProjectWindow(NewProjectViewModel model) : this() => DataContext = model;

    private NewProjectViewModel Model => (NewProjectViewModel)DataContext!;

    /// <summary>True when the project was written (the caller opens <see cref="NewProjectViewModel.CreatedThconfig"/>).</summary>
    public bool Confirmed { get; private set; }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (Model.Create() is { } problem) { Model.Message = problem; return; }
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = ThIDE.Resources.Tr.Get("NewProject_Folder") });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) Model.TargetFolder = path;
    }

    private async void OnImportFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = ThIDE.Resources.Tr.Get("NewProject_ImportFolder") });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) Model.Message = Model.Import(path) ?? string.Empty;
    }

    private async void OnImportZip(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = ThIDE.Resources.Tr.Get("NewProject_ImportZip"),
            FileTypeFilter = [new FilePickerFileType("Zip") { Patterns = ["*.zip"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) Model.Message = Model.Import(path) ?? string.Empty;
    }

    private void OnRemove(object? sender, RoutedEventArgs e) => Model.RemoveSelected();
}
