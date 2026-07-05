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
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Views.Docking;

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

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        // The handler is attached to the content Border (a descendant of the TreeViewItem), so
        // prefer that Border's own node; fall back to the event source / selection.
        var node = (sender as Control)?.DataContext as WorkspaceTreeNode
                   ?? NodeFromSource(e.Source) ?? Explorer?.Selected;
        if (node is null || Explorer is not { } ex) return;

        // Double-tapping the text row performs the node's action: open a file / go to a
        // definition for object + file nodes, expand for folders. We ALWAYS mark the event
        // handled so it can't bubble up to the TreeViewItem's built-in expand-on-double-tap —
        // that is what previously folded/unfolded the node out from under the open action (#5).
        // Fold/unfold now happens only via the chevron arrow (which is outside this Border).
        e.Handled = true;
        ex.ActivateCommand.Execute(node);
    }

    // ----- : drag a file node into the editor to insert an input/source line ----

    private WorkspaceTreeNode? _dragNode;
    private PointerPressedEventArgs? _dragPress;
    private Point _dragStart;

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        var node = (sender as Control)?.DataContext as WorkspaceTreeNode;
        if (node?.FullPath is { } p && File.Exists(p)) { _dragNode = node; _dragPress = e; _dragStart = e.GetPosition(null); }
    }

    private async void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragNode is null || _dragPress is null) return;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) { _dragNode = null; _dragPress = null; return; }
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < 4 && Math.Abs(pos.Y - _dragStart.Y) < 4) return;

        var node = _dragNode; var press = _dragPress;
        _dragNode = null; _dragPress = null;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(Editor.TherionTextEditor.InsertPathFormat, node.FullPath!));
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
        }
        catch { /* drag unsupported / cancelled */ }
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
                        case "setactive": mi.IsVisible = IsThconfigNode(Ctx()); break;
                    }
    }

    /// <summary>True when the node is a .thconfig file (drives the "set active" entry, #7).</summary>
    private static bool IsThconfigNode(WorkspaceTreeNode? node)
    {
        if (node?.FullPath is not { } path) return false;
        if (string.Equals(node.Kind, "thconfig", StringComparison.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(path);
        return ext.Equals(".thconfig", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".thc", StringComparison.OrdinalIgnoreCase);
    }

    // Make the right-clicked .thconfig the workspace's active configuration (#7/#8).
    // Switching the active config rebuilds the shared object graph, which the session's
    // Changed event propagates to the dropdown selector, the window title, every open
    // editor's banner/highlighting and the diagnostics — so we only need to kick it off
    // here and warn the user if the file can't be made active rather than failing silently.
    private async void OnCtxSetActiveThconfig(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is not { } raw) return;
        if (Service<IDocumentService>() is not { } documents)
        {
            await ShowWarning(ThIDE.Resources.Tr.Get("Dlg_SetActiveTitle"), ThIDE.Resources.Tr.Get("Dlg_NoWorkspace")).ConfigureAwait(true);
            return;
        }

        // Normalize to a full path so this matches exactly what the dropdown selector passes
        // (the combobox uses the candidate's already-normalized FullPath). Report the precise
        // reason on failure rather than a single generic message (#2/#8).
        string path;
        try { path = System.IO.Path.GetFullPath(raw); }
        catch { await ShowWarning(ThIDE.Resources.Tr.Get("Dlg_SetActiveTitle"), ThIDE.Resources.Tr.Get("Dlg_InvalidPath") + "\n" + raw).ConfigureAwait(true); return; }

        if (!System.IO.File.Exists(path))
        {
            await ShowWarning(ThIDE.Resources.Tr.Get("Dlg_SetActiveTitle"), ThIDE.Resources.Tr.Get("Dlg_FileGone") + "\n" + path).ConfigureAwait(true);
            return;
        }

        bool ok;
        string? error = null;
        try { ok = await documents.ActivateThconfigAsync(path).ConfigureAwait(true); }
        catch (Exception ex) { ok = false; error = ex.Message; }

        if (!ok)
            await ShowWarning(ThIDE.Resources.Tr.Get("Dlg_SetActiveTitle"),
                error ?? string.Format(ThIDE.Resources.Tr.Get("Dlg_NotThconfigFmt"), System.IO.Path.GetFileName(path)))
                .ConfigureAwait(true);
    }

    private Task ShowWarning(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            return new MessageDialog(title, message).ShowAsync(owner);
        return Task.CompletedTask;
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

    private async void OnCtxDelete(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is not { } path) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        // confirm before deleting. The message states whether it's undoable (trash) or
        // permanent, so the user knows the stakes.
        var fileOps = Service<IFileOperations>();
        var label = fileOps?.DeleteActionLabel ?? "Delete";
        bool undoable = fileOps?.DeleteIsUndoable ?? false;
        var name = System.IO.Path.GetFileName(path);
        var message = undoable
            ? $"{label}:\n\n{name}\n\nYou can restore it from the trash/recycle bin."
            : $"Permanently delete?\n\n{name}\n\nThis cannot be undone.";

        if (!await new ConfirmDialog("Delete", message, label).ShowAsync(owner).ConfigureAwait(true)) return;
        // Cross-platform delete (recycle bin / trash / permanent) via the OS-specific service.
        if (fileOps?.Delete(path) == true) Explorer?.Refresh();
    }

    private void OnCtxCopyFull(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is { } path) SetClipboard(path);
    }

    private void OnCtxCopyRel(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is not { } path) return;
        // Relativize against the workspace ROOT DIRECTORY. Explorer.RootPath is view-dependent — in the
        // relational view it holds the first root *file* (e.g. the thconfig), which would make
        // Relativize fall back to the full path — so prefer the session's root directory.
        var root = Service<IWorkspaceSession>()?.RootPath ?? Explorer?.RootPath;
        SetClipboard(WorkspacePathFormatter.Relativize(root, path));
    }

    private void OnCtxRefresh(object? sender, RoutedEventArgs e) => Explorer?.Refresh();

    private void OnCtxShellMenu(object? sender, RoutedEventArgs e)
    {
        if (Ctx()?.FullPath is not { } path) return;
        // Delegate to the platform service; the interop lives in WindowsNativeContextMenuService.
        var hwnd = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        // Defer the native shell menu until AFTER Avalonia's own ContextMenu has fully closed.
        // Running the shell's modal TrackPopupMenuEx loop synchronously from inside this click
        // handler re-enters the popup that is still tearing down, corrupting shell state and
        // raising an AccessViolationException (item #13). Posting it breaks that re-entrancy.
        var nativeMenu = Service<INativeContextMenuService>();
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => nativeMenu?.TryShow(hwnd, path),
            Avalonia.Threading.DispatcherPriority.Background);
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
