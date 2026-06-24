// Preview window for symbol rename (#1). Shows a collapsible tree of all proposed
// text changes (file nodes → change subitems) before applying. Returns true when
// the user clicks Apply, false on Cancel.

using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TherionProc.Views;

internal sealed class RenamePreviewWindow : Window
{
    internal RenamePreviewWindow(
        IReadOnlyList<RenameFileChanges> changes,
        string oldName,
        string newName)
    {
        Title = $"Rename '{oldName}' → '{newName}'";
        Width = 560;
        Height = 460;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tree = new TreeView { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var fc in changes)
        {
            var fileItem = new TreeViewItem
            {
                Header = $"{Path.GetFileName(fc.FilePath)}  ({fc.Hits.Count} change{(fc.Hits.Count == 1 ? "" : "s")})",
                IsExpanded = true,
            };
            ToolTip.SetTip(fileItem, fc.FilePath);
            foreach (var (start, _) in fc.Hits)
            {
                var (line, col) = OffsetToLineCol(fc.FileText, start);
                var lineText = GetLineText(fc.FileText, start).Trim();
                fileItem.Items.Add(new TreeViewItem
                {
                    Header = $"Line {line}, Col {col}:  {lineText}",
                });
            }
            tree.Items.Add(fileItem);
        }

        var summary = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            Text = BuildSummary(changes, oldName, newName),
        };

        var applyBtn  = new Button { Content = "Apply",  IsDefault = true,  MinWidth = 80 };
        var cancelBtn = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        applyBtn.Click  += (_, _) => Close(true);
        cancelBtn.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelBtn, applyBtn },
        };

        DockPanel.SetDock(summary,  Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(buttons,  Avalonia.Controls.Dock.Bottom);

        Content = new DockPanel
        {
            Margin = new Thickness(12),
            LastChildFill = true,
            Children = { summary, buttons, tree },
        };
    }

    private static string BuildSummary(IReadOnlyList<RenameFileChanges> changes, string oldName, string newName)
    {
        int total = 0;
        foreach (var fc in changes) total += fc.Hits.Count;
        return $"Rename '{oldName}' → '{newName}': {total} occurrence{(total == 1 ? "" : "s")} across {changes.Count} file{(changes.Count == 1 ? "" : "s")}.";
    }

    private static (int Line, int Col) OffsetToLineCol(string text, int offset)
    {
        int line = 1, col = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return (line, col);
    }

    private static string GetLineText(string text, int offset)
    {
        int start = offset;
        while (start > 0 && text[start - 1] != '\n') start--;
        int end = offset;
        while (end < text.Length && text[end] != '\n') end++;
        var line = text.Substring(start, end - start);
        return line.Length > 80 ? line[..80] + "…" : line;
    }
}
