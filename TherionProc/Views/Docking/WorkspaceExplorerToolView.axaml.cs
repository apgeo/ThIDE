using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Therion.Build;
using TherionProc.Services;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;
using TherionProc.Views;

namespace TherionProc.Views.Docking;

public partial class WorkspaceExplorerToolView : UserControl
{
    private WorkspaceExplorerViewModel? _explorer;

    // The node the context menu was opened on (captured on ContextRequested, which fires
    // before the menu's Opening — so we never depend on the ContextMenu's DataContext).
    private WorkspaceTreeNode? _ctxNode;

    public WorkspaceExplorerToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_explorer is not null) _explorer.PropertyChanged -= OnExplorerPropertyChanged;
        _explorer = (DataContext as WorkspaceExplorerToolViewModel)?.Explorer;
        if (_explorer is not null) _explorer.PropertyChanged += OnExplorerPropertyChanged;
    }

    private WorkspaceExplorerViewModel? Explorer => (DataContext as WorkspaceExplorerToolViewModel)?.Explorer;

    // Scroll the revealed (selected) or hover-highlighted node into view (#4/#8/#9).
    private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceExplorerViewModel.Selected) && _explorer?.Selected is { } sel)
            ScrollTo(sel);
        else if (e.PropertyName == nameof(WorkspaceExplorerViewModel.HighlightedNode) &&
                 _explorer?.HighlightedNode is { } hl)
            ScrollTo(hl);
    }

    private void ScrollTo(WorkspaceTreeNode node)
    {
        if (this.FindControl<TreeView>("Tree") is { } tree)
            try { tree.ScrollIntoView(node); } catch { /* best-effort */ }
    }

    private void OnNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (Explorer is { Selected: { } node } ex) ex.ActivateCommand.Execute(node);
    }

    // ----- file-explorer context menu ----------------------------------------

    // Capture the right-clicked node (and select it) before the menu opens.
    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        _ctxNode = NodeFromSource(e.Source);
        if (_ctxNode is not null && Explorer is { } ex) ex.Selected = _ctxNode;
    }

    private static WorkspaceTreeNode? NodeFromSource(object? source)
    {
        var element = source as StyledElement;
        while (element is not null)
        {
            if (element.DataContext is WorkspaceTreeNode node) return node;
            element = element.Parent;
        }
        return null;
    }

    private WorkspaceTreeNode? Ctx() => _ctxNode ?? Explorer?.Selected;

    // Only show the menu on real filesystem nodes. Adapt per platform via the abstractions
    // (no raw OS checks here): show the native-shell entry only where supported, and label
    // "Delete" with what it actually does on this OS (Recycle Bin / Trash / permanent).
    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (Ctx()?.FullPath is null) { e.Cancel = true; return; }

        var fileOps = Service<IFileOperations>();
        var nativeMenu = Service<INativeContextMenuService>();
        if (sender is ContextMenu menu)
            foreach (var item in menu.Items)
                if (item is MenuItem mi)
                    switch (mi.Tag as string)
                    {
                        case "nativemenu": mi.IsVisible = nativeMenu?.IsSupported ?? false; break;
                        case "delete" when fileOps is not null: mi.Header = fileOps.DeleteActionLabel; break;
                    }
    }

    private void OnCtxOpen(object? sender, RoutedEventArgs e)
    {
        if (Ctx() is { } node) Explorer?.ActivateCommand.Execute(node);
    }

    private void OnCtxReveal(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is { } path) Service<IShellOpener>()?.RevealInFileManager(path);
    }

    private async void OnCtxNewFile(object? sender, RoutedEventArgs e)
    {
        if (DirectoryFor(Ctx()) is not { } dir) return;
        var name = await PromptName("New File", "File name:", "new.th").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;

        var path = Path.Combine(dir, name);
        try { if (!File.Exists(path)) File.Create(path).Dispose(); } catch { return; }
        Explorer?.Refresh();
        Explorer?.RevealFile(path);
    }

    private async void OnCtxNewFolder(object? sender, RoutedEventArgs e)
    {
        if (DirectoryFor(Ctx()) is not { } dir) return;
        var name = await PromptName("New Folder", "Folder name:", "New Folder").ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;

        var path = Path.Combine(dir, name);
        try { Directory.CreateDirectory(path); } catch { return; }
        Explorer?.Refresh();
        Explorer?.RevealFile(path);
    }

    private void OnCtxDelete(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is not { } path) return;
        // Cross-platform delete (recycle bin / trash / permanent) via the OS-specific service.
        if (Service<IFileOperations>()?.Delete(path) == true) Explorer?.Refresh();
    }

    private void OnCtxCopyFull(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is { } path) SetClipboard(path);
    }

    private void OnCtxCopyRel(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is { } path)
            SetClipboard(WorkspacePathFormatter.Relativize(Explorer?.RootPath, path));
    }

    private void OnCtxRefresh(object? sender, RoutedEventArgs e) => Explorer?.Refresh();

    private void OnCtxShellMenu(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is not { } path) return;
        // Delegate to the platform service; the interop lives in WindowsNativeContextMenuService.
        var hwnd = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        Service<INativeContextMenuService>()?.TryShow(hwnd, path);
    }

    // ----- helpers ------------------------------------------------------------

    private async Task<string?> PromptName(string title, string prompt, string initial)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return null;
        return await new InputDialog(title, prompt, initial).ShowAsync(owner).ConfigureAwait(true);
    }

    private static string? DirectoryFor(WorkspaceTreeNode? node)
    {
        if (node?.FullPath is not { } path) return null;
        if (Directory.Exists(path)) return path;
        return Path.GetDirectoryName(path);
    }

    private void SetClipboard(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) _ = clipboard.SetTextAsync(text);
    }

    private static T? Service<T>() where T : class
    {
        try { return AppServices.Provider.GetService<T>(); }
        catch { return null; }
    }
}
