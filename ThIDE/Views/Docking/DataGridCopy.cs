// Reusable right-click "copy" context menu for the Overview-panel DataGrids.
//
// Set `docking:DataGridCopy.Enable="True"` on any DataGrid and it gains a context menu with:
//   • Copy               — the value of the right-clicked cell
//   • Copy row           — the selected row as tab-separated values
//   • Copy row formatted — the selected row as a one-row Markdown table (with headers)
//   • Copy all           — every row as TSV (header line + rows)
//   • Copy all formatted — every row as a Markdown table
//
// Values come from the columns' bindings (so virtualized rows copy too); the single-cell copy
// reads the clicked cell's rendered text. Headers fall back to the binding path when a column has none.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ThIDE.Resources;
using ThIDE.Services;
using Therion.Semantics;

namespace ThIDE.Views.Docking;

public static class DataGridCopy
{
    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Enable", typeof(DataGridCopy));

    public static bool GetEnable(DataGrid grid) => grid.GetValue(EnableProperty);
    public static void SetEnable(DataGrid grid, bool value) => grid.SetValue(EnableProperty, value);

    private static readonly ConditionalWeakTable<DataGrid, object> Attached = new();

    static DataGridCopy()
    {
        // Non-generic stream so e.NewValue is a plain object we can pattern-match.
        System.IObservable<AvaloniaPropertyChangedEventArgs> changed = EnableProperty.Changed;
        changed.AddClassHandler<DataGrid>((grid, e) => { if (e.NewValue is true) Attach(grid); });
    }

    private static void Attach(DataGrid grid)
    {
        if (Attached.TryGetValue(grid, out _)) return;
        Attached.Add(grid, new object());

        // Remember the cell (and select the row) under a right-click, before the menu opens.
        DataGridCell? clicked = null;
        grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
            var src = e.Source as Visual;
            clicked = src?.FindAncestorOfType<DataGridCell>();
            if (src?.FindAncestorOfType<DataGridRow>()?.DataContext is { } item) grid.SelectedItem = item;
        }, RoutingStrategies.Tunnel);

        var menu = new ContextMenu();
        menu.Items.Add(Item(Tr.Get("Grid_Copy"), () => CopyCell(clicked)));
        menu.Items.Add(Item(Tr.Get("Grid_CopyRow"), () => CopyRow(grid, formatted: false)));
        menu.Items.Add(Item(Tr.Get("Grid_CopyRowFormatted"), () => CopyRow(grid, formatted: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Tr.Get("Grid_CopyAll"), () => CopyAll(grid, formatted: false)));
        menu.Items.Add(Item(Tr.Get("Grid_CopyAllFormatted"), () => CopyAll(grid, formatted: true)));
        grid.ContextMenu = menu;
    }

    private static MenuItem Item(string header, System.Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    // ---- copy actions ----

    private static void CopyCell(DataGridCell? cell)
    {
        var text = cell?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
        if (!string.IsNullOrEmpty(text)) ClipboardHelper.SetText(text);
    }

    private static void CopyRow(DataGrid grid, bool formatted)
    {
        if (grid.SelectedItem is not { } item) return;
        var (headers, paths) = Columns(grid);
        var values = paths.Select(p => Resolve(item, p)).ToList();
        ClipboardHelper.SetText(formatted
            ? DataExport.ToMarkdown(headers, new[] { (IReadOnlyList<string>)values })
            : string.Join("\t", values));
    }

    private static void CopyAll(DataGrid grid, bool formatted)
    {
        var (headers, paths) = Columns(grid);
        var items = (grid.ItemsSource as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
        var rows = items.Select(it => (IReadOnlyList<string>)paths.Select(p => Resolve(it, p)).ToList()).ToList();
        if (formatted)
        {
            ClipboardHelper.SetText(DataExport.ToMarkdown(headers, rows));
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", headers));
        foreach (var r in rows) sb.AppendLine(string.Join("\t", r));
        ClipboardHelper.SetText(sb.ToString());
    }

    // ---- column / value extraction ----

    // Visible bound columns → (header text, binding path). Header falls back to the path.
    private static (List<string> Headers, List<string> Paths) Columns(DataGrid grid)
    {
        var headers = new List<string>();
        var paths = new List<string>();
        foreach (var col in grid.Columns)
        {
            if (!col.IsVisible || col is not DataGridBoundColumn bound) continue;
            if ((bound.Binding as Binding)?.Path is not { Length: > 0 } path) continue;
            paths.Add(path);
            headers.Add(col.Header?.ToString() is { Length: > 0 } h ? h : path);
        }
        return (headers, paths);
    }

    // Resolve a (possibly dotted) binding path against an item via reflection.
    private static string Resolve(object? item, string path)
    {
        object? cur = item;
        foreach (var part in path.Split('.'))
        {
            if (cur is null) return string.Empty;
            var pi = cur.GetType().GetProperty(part);
            if (pi is null) return string.Empty;
            cur = pi.GetValue(cur);
        }
        return cur?.ToString() ?? string.Empty;
    }
}
