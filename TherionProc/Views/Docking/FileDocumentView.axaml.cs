using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Therion.Core;
using TherionProc.Editor;
using TherionProc.Services;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class FileDocumentView : UserControl
{
    private FileDocumentViewModel? _vm;

    public FileDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        if (this.FindControl<TherionTextEditor>("Editor") is { } editor)
        {
            editor.OpenFileRequested += OnOpenFileRequested;
            editor.NavigateToSpanRequested += OnNavigateToSpanRequested;
        }
    }

    private void OnOpenFileRequested(object? sender, string path)
    {
        // The editor resolved an input/load target; open it as a document.
        _ = TryDocuments()?.OpenFileAsync(path);
    }

    private void OnNavigateToSpanRequested(object? sender, SourceSpan span)
    {
        // A cross-file reference resolved into another file — open it and scroll/flash.
        _ = TryDocuments()?.NavigateToSpanAsync(span);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.ScrollToSpanRequested -= OnScrollRequested;
        _vm = DataContext as FileDocumentViewModel;
        if (_vm is not null)
        {
            _vm.ScrollToSpanRequested += OnScrollRequested;
            ApplyPendingScrollDeferred();
        }
    }

    private void OnScrollRequested(object? sender, SourceSpan span)
    {
        this.FindControl<TherionTextEditor>("Editor")?.ScrollTo(span);
        _vm?.ClearPendingScroll();
    }

    // A document opened via navigation may bind its view after the scroll was requested;
    // replay the pending target once the editor is laid out.
    private void ApplyPendingScrollDeferred()
    {
        if (_vm?.PendingScroll is not { } span) return;
        Dispatcher.UIThread.Post(() =>
        {
            this.FindControl<TherionTextEditor>("Editor")?.ScrollTo(span);
            _vm?.ClearPendingScroll();
        }, DispatcherPriority.Loaded);
    }

    private static IDocumentService? TryDocuments()
    {
        try { return AppServices.Provider.GetService<IDocumentService>(); }
        catch { return null; } // design-time / no container
    }
}
