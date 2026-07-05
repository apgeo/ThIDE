using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ThIDE.Services;

namespace ThIDE.Views;

public partial class BookmarksWindow : Window
{
    private IBookmarksService? _service;
    private IDocumentService? _docs;

    public BookmarksWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            _service = AppServices.Provider.GetService<IBookmarksService>();
            _docs    = AppServices.Provider.GetService<IDocumentService>();
        }
        catch { }

        Refresh();
        if (_service is not null) _service.BookmarksChanged += (_, _) => Refresh();
    }

    private void Refresh()
    {
        if (this.FindControl<DataGrid>("Grid") is { } grid)
            grid.ItemsSource = _service?.Bookmarks ?? Array.Empty<BookmarkEntry>();
    }

    private BookmarkEntry? SelectedEntry()
        => this.FindControl<DataGrid>("Grid")?.SelectedItem as BookmarkEntry;

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntry() is { } entry) _service?.RemoveBookmark(entry);
    }

    private void OnNavigateClick(object? sender, RoutedEventArgs e) => NavigateSelected();

    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => NavigateSelected();

    private void NavigateSelected()
    {
        if (SelectedEntry() is not { } entry || _docs is null) return;
        var span = new Therion.Core.SourceSpan(
            entry.FilePath,
            new Therion.Core.SourceLocation(entry.Line, 1),
            new Therion.Core.SourceLocation(entry.Line, 1),
            0, 0);
        _ = _docs.NavigateToSpanAsync(span);
    }
}
