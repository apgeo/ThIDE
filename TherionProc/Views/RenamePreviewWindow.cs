// Preview window for symbol rename (#1). Shows a collapsible tree of all proposed
// text changes (file nodes → change subitems) before applying. Returns true when
// the user clicks Apply, false on Cancel. Two opt-in expansions are offered as
// checkboxes: rename same-named symbols in other surveys/files (≈ replace-all), and
// rename inside comments.

using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TherionProc.Views;

internal sealed class RenamePreviewWindow : Window
{
    /// <summary>True when the user opted to also rename occurrences inside comments.</summary>
    public bool IncludeComments { get; private set; }

    /// <summary>True when the user opted to also rename every other same-named symbol (≈ replace-all).</summary>
    public bool IncludeSameName { get; private set; }

    private const string SameNameHelp =
        "Rename normally changes only this exact station — the one in this survey — and the references " +
        "that point at it (e.g. equate commands and cross-file \"@\" links). A different survey can have " +
        "its own station that happens to share this name; those are left untouched.\n\n" +
        "Check this box to also rename every other station or survey that merely shares this name, in any " +
        "survey or file. This is essentially a \"replace all occurrences of the text\" — it can rewrite " +
        "unrelated stations that happen to have the same name. Use with care.";

    internal RenamePreviewWindow(
        IReadOnlyList<RenameFileChanges> changes,
        string oldName,
        string newName)
        : this(changes, System.Array.Empty<RenameFileChanges>(),
               System.Array.Empty<RenameFileChanges>(), oldName, newName) { }

    internal RenamePreviewWindow(
        IReadOnlyList<RenameFileChanges> changes,
        IReadOnlyList<RenameFileChanges> sameNameChanges,
        IReadOnlyList<RenameFileChanges> commentChanges,
        string oldName,
        string newName)
    {
        Title = $"Rename '{oldName}' → '{newName}'";
        Width = 560;
        Height = 480;
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

        int sameNameCount = AddGroup(tree, sameNameChanges, "Same-named symbols in other surveys / files");
        int commentCount  = AddGroup(tree, commentChanges,  "In comments");

        // ---- opt-in: rename every other same-named symbol (≈ replace-all) ----
        var sameNameCheck = new CheckBox
        {
            Content = $"Also rename {sameNameCount} same-named occurrence{(sameNameCount == 1 ? "" : "s")} in other surveys / files",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        sameNameCheck.IsCheckedChanged += (_, _) => IncludeSameName = sameNameCheck.IsChecked == true;

        var help = new Button
        {
            Content = "?",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = new Flyout
            {
                Content = new TextBlock { Text = SameNameHelp, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
            },
        };
        ToolTip.SetTip(help, "What does this do?");

        var sameNameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            IsVisible = sameNameCount > 0,
            Children = { sameNameCheck, help },
        };

        // ---- opt-in: rename inside comments ----
        var commentsCheck = new CheckBox
        {
            Content = $"Also rename {commentCount} occurrence{(commentCount == 1 ? "" : "s")} in comments",
            IsChecked = false,
            IsVisible = commentCount > 0,
            Margin = new Thickness(0, 0, 0, 8),
        };
        commentsCheck.IsCheckedChanged += (_, _) => IncludeComments = commentsCheck.IsChecked == true;

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

        DockPanel.SetDock(summary,      Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(buttons,      Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(commentsCheck, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(sameNameRow,  Avalonia.Controls.Dock.Bottom);

        Content = new DockPanel
        {
            Margin = new Thickness(12),
            LastChildFill = true,
            Children = { summary, buttons, commentsCheck, sameNameRow, tree },
        };
    }

    /// <summary>Adds a collapsed "group" node summarising an opt-in change set; returns its total hit count.</summary>
    private static int AddGroup(TreeView tree, IReadOnlyList<RenameFileChanges> group, string label)
    {
        int count = 0;
        foreach (var fc in group) count += fc.Hits.Count;
        if (count == 0) return 0;

        var node = new TreeViewItem { Header = $"{label}  ({count})", IsExpanded = false };
        foreach (var fc in group)
        {
            var fileItem = new TreeViewItem
            {
                Header = $"{Path.GetFileName(fc.FilePath)}  ({fc.Hits.Count})",
                IsExpanded = false,
            };
            ToolTip.SetTip(fileItem, fc.FilePath);
            foreach (var (start, _) in fc.Hits)
            {
                var (line, col) = OffsetToLineCol(fc.FileText, start);
                fileItem.Items.Add(new TreeViewItem
                {
                    Header = $"Line {line}, Col {col}:  {GetLineText(fc.FileText, start).Trim()}",
                });
            }
            node.Items.Add(fileItem);
        }
        tree.Items.Add(node);
        return count;
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
