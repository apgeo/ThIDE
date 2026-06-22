using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Therion.Build;
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
        AttachedToVisualTree += (_, _) => RestoreViewState();
        if (this.FindControl<TherionTextEditor>("Editor") is { } editor)
        {
            editor.OpenFileRequested += OnOpenFileRequested;
            editor.OpenExternalRequested += OnOpenExternalRequested;
            editor.NavigateToSpanRequested += OnNavigateToSpanRequested;
            editor.CaretMoved += OnCaretMoved;
            editor.HoverTargetChanged += OnHoverTargetChanged;
            editor.FindReferencesRequested += OnFindReferencesRequested;
        }
    }

    private void OnFindReferencesRequested(object? sender, string term)
        => TryDocuments()?.RequestFindReferences(term);

    // Ask the Workspace Explorer to reveal the hovered link's target (gated by its toggle, #8).
    private void OnHoverTargetChanged(object? sender, SourceSpan? target)
    {
        if (target is { } span) TryDocuments()?.RequestRevealInWorkspace(span);
    }

    // An export/output link (.lox/.pdf/.3d/...) — open in the OS default viewer (#15)
    // through the cross-platform IShellOpener (Windows ShellExecute / macOS `open` /
    // Linux `xdg-open`), rather than a raw Process.Start whose UseShellExecute=true only
    // resolves a default handler reliably on Windows.
    private void OnOpenExternalRequested(object? sender, string path)
        => TryShellOpener()?.Open(path);

    private void OnCaretMoved(object? sender, SourceSpan span)
    {
        if (_vm is null) return;
        _vm.SetCaret(span);
        _vm.SavedCaretOffset = span.StartOffset; // remember position for tab switches (#11)

        // Feed the caret into the back/forward history, but skip moves driven by
        // highlighted-term navigation (Shift+F12 cycling), per #1.
        bool termNav = (sender as TherionTextEditor)?.IsTermNavigating ?? false;
        TryDocuments()?.ReportCaret(span, termNav);
    }

    // Restore the caret when this tab is shown again — unless a navigation scroll is pending.
    private void RestoreViewState()
    {
        if (_vm is null || _vm.PendingScroll is not null || _vm.SavedCaretOffset <= 0) return;
        var offset = _vm.SavedCaretOffset;
        Dispatcher.UIThread.Post(
            () => this.FindControl<TherionTextEditor>("Editor")?.RestoreCaret(offset),
            DispatcherPriority.Loaded);
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

    private static IShellOpener? TryShellOpener()
    {
        try { return AppServices.Provider.GetService<IShellOpener>(); }
        catch { return null; } // design-time / no container
    }
}
