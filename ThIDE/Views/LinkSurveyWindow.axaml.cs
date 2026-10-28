using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class LinkSurveyWindow : Window
{
    public LinkSurveyWindow() : this(null!) { }

    public LinkSurveyWindow(LinkSurveyViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        if (model is null) return;
        model.CopyRequested += async (_, text) =>
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        };
        model.Applied += (_, _) => { Confirmed = true; Close(); };
        model.FullPreviewRequested += (_, _) => PreviewWindow.ShowLink(this, model);
    }

    /// <summary>True when the lines were written.</summary>
    public bool Confirmed { get; private set; }

    private LinkSurveyViewModel Model => (LinkSurveyViewModel)DataContext!;

    private async void OnInsert(object? sender, RoutedEventArgs e) => await Model.InsertAsync();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowseSource(object? sender, RoutedEventArgs e)
    {
        // LoadSourceAsync reports its own failures in the dialog; nothing may escape an async void.
        try { await BrowseSourceAsync(); }
        catch (Exception ex) { Model.SourceStatus = ex.Message; }
    }

    private async System.Threading.Tasks.Task BrowseSourceAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = ThIDE.Resources.Tr.Get("LinkSurvey_PickSource"),
            FileTypeFilter = [new FilePickerFileType("Therion") { Patterns = ["*.th"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await Model.LoadSourceAsync(path);
    }

    private async void OnBrowseTarget(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = ThIDE.Resources.Tr.Get("LinkSurvey_PickTarget"),
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) Model.TargetDirectory = path;
    }
}
