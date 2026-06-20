using System;
using Avalonia.Controls;
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
            editor.OpenFileRequested += OnOpenFileRequested;
    }

    private void OnOpenFileRequested(object? sender, string path)
    {
        // The editor resolved an input/load target; open it as a document.
        IDocumentService? docs = null;
        try { docs = AppServices.Provider.GetService<IDocumentService>(); }
        catch { /* design-time / no container */ }
        _ = docs?.OpenFileAsync(path);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.ScrollToSpanRequested -= OnScrollRequested;
        _vm = DataContext as FileDocumentViewModel;
        if (_vm is not null) _vm.ScrollToSpanRequested += OnScrollRequested;
    }

    private void OnScrollRequested(object? sender, SourceSpan span) =>
        this.FindControl<TherionTextEditor>("Editor")?.ScrollTo(span);
}
