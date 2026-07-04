// Preview window for symbol rename (#1). Shows a collapsible tree of all proposed text changes
// (file nodes → change subitems) before applying. Returns true when the user clicks Apply, false
// on Cancel. Three opt-in expansions are offered, each a checkbox + a "?" help flyout:
//   1. rename equate-linked same-named stations in other surveys (keep the equated point connected),
//   2. rename every same-named station/survey elsewhere (≈ replace-all),
//   3. rename inside comments.
// Entries that will actually be replaced under the current checkbox combination are tinted pale blue
// (the base changes always are); clicking an entry navigates to its file/line without closing this window.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Therion.Core;

namespace TherionProc.Views;

internal sealed class RenamePreviewWindow : Window
{
    /// <summary>Rename the stations an <c>equate</c> links to this one that share its name (other surveys).</summary>
    public bool IncludeEquateLinked { get; private set; }

    /// <summary>Rename every other same-named symbol regardless of survey/file (≈ replace-all).</summary>
    public bool IncludeSameName { get; private set; }

    /// <summary>Rename occurrences inside <c>#</c> comments.</summary>
    public bool IncludeComments { get; private set; }

    // Pale blue tint (translucent so it reads on both light and dark themes) for "will be replaced".
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x21, 0x96, 0xF3));

    private readonly Func<SourceSpan, Task>? _navigate;

    private const string EquateHelp =
        "Stations in different surveys can be declared to be the same physical point with an \"equate\" " +
        "command — e.g. equate 1@a 1@b makes station 1 in survey a and station 1 in survey b the same point.\n\n" +
        "Check this to also rename the other equated stations that share this name, following the equate " +
        "links across surveys and files, so the equated connection keeps referring to the same name. Unlike " +
        "\"same name in other surveys\" below, this only touches stations actually linked to this one by an equate.";

    private const string SameNameHelp =
        "Rename normally changes only this exact station — the one in this survey — and the references " +
        "that point at it (e.g. equate commands and cross-file \"@\" links). A different survey can have " +
        "its own station that happens to share this name; those are left untouched.\n\n" +
        "Check this box to also rename every other station or survey that merely shares this name, in any " +
        "survey or file. This is essentially a \"replace all occurrences of the text\" — it can rewrite " +
        "unrelated stations that happen to have the same name. Use with care.";

    private const string CommentsHelp =
        "The rename deliberately skips \"#\" comments (they are prose, not code). Check this to also rewrite " +
        "the name where it appears as a whole word inside comments. It is a plain text match, so it can catch " +
        "unrelated mentions — review the highlighted entries first.";

    internal RenamePreviewWindow(
        IReadOnlyList<RenameFileChanges> changes,
        string oldName,
        string newName)
        : this(changes, Array.Empty<RenameFileChanges>(), Array.Empty<RenameFileChanges>(),
               Array.Empty<RenameFileChanges>(), oldName, newName) { }

    internal RenamePreviewWindow(
        IReadOnlyList<RenameFileChanges> changes,
        IReadOnlyList<RenameFileChanges> equateLinkedChanges,
        IReadOnlyList<RenameFileChanges> sameNameChanges,
        IReadOnlyList<RenameFileChanges> commentChanges,
        string oldName,
        string newName,
        Func<SourceSpan, Task>? navigate = null)
    {
        _navigate = navigate;

        Title = $"Rename '{oldName}' → '{newName}'";
        Width = 580;
        Height = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tree = new TreeView { Margin = new Thickness(0, 0, 0, 8) };

        // Base changes — always applied, so always tinted.
        var baseTint = new List<Border>();
        foreach (var fc in changes) AddFileNode(tree, fc, baseTint);
        SetTint(baseTint, true);

        var equateTint = new List<Border>();
        int equateCount = AddGroup(tree, equateLinkedChanges, "Equate-linked stations (same name, other surveys)", equateTint);
        var sameTint = new List<Border>();
        int sameCount = AddGroup(tree, sameNameChanges, "Same-named symbols in other surveys / files", sameTint);
        var commentTint = new List<Border>();
        int commentCount = AddGroup(tree, commentChanges, "In comments", commentTint);

        // ---- opt-in checkboxes (equate-linked first, then replace-all, then comments) ----
        var equateCheck = new CheckBox
        {
            Content = $"Also rename {equateCount} equate-linked station{(equateCount == 1 ? "" : "s")} (same name, other surveys)",
        };
        equateCheck.IsCheckedChanged += (_, _) => { IncludeEquateLinked = equateCheck.IsChecked == true; SetTint(equateTint, IncludeEquateLinked); };

        var sameCheck = new CheckBox
        {
            Content = $"Also rename {sameCount} same-named occurrence{(sameCount == 1 ? "" : "s")} in other surveys / files",
        };
        sameCheck.IsCheckedChanged += (_, _) => { IncludeSameName = sameCheck.IsChecked == true; SetTint(sameTint, IncludeSameName); };

        var commentCheck = new CheckBox
        {
            Content = $"Also rename {commentCount} occurrence{(commentCount == 1 ? "" : "s")} in comments",
        };
        commentCheck.IsCheckedChanged += (_, _) => { IncludeComments = commentCheck.IsChecked == true; SetTint(commentTint, IncludeComments); };

        var checks = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                MakeCheckRow(equateCheck, equateCount > 0, EquateHelp),
                MakeCheckRow(sameCheck, sameCount > 0, SameNameHelp),
                MakeCheckRow(commentCheck, commentCount > 0, CommentsHelp),
            },
        };

        var summary = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            Text = BuildSummary(changes, oldName, newName),
        };

        var applyBtn  = new Button { Content = "Apply",  IsDefault = true, MinWidth = 80 };
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

        DockPanel.SetDock(summary, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(checks,  Avalonia.Controls.Dock.Bottom);

        Content = new DockPanel
        {
            Margin = new Thickness(12),
            LastChildFill = true,
            Children = { summary, buttons, checks, tree },
        };
    }

    /// <summary>Builds a file node with one leaf per hit; leaves navigate on click and are tint-tracked.</summary>
    private void AddFileNode(ItemsControl parent, RenameFileChanges fc, List<Border> tint)
    {
        var fileItem = new TreeViewItem
        {
            Header = $"{Path.GetFileName(fc.FilePath)}  ({fc.Hits.Count} change{(fc.Hits.Count == 1 ? "" : "s")})",
            IsExpanded = true,
        };
        ToolTip.SetTip(fileItem, fc.FilePath);
        foreach (var (start, length) in fc.Hits)
        {
            var (line, col) = OffsetToLineCol(fc.FileText, start);
            var lineText = GetLineText(fc.FileText, start).Trim();
            var border = new Border
            {
                Padding = new Thickness(3, 0),
                CornerRadius = new CornerRadius(2),
                Child = new TextBlock { Text = $"Line {line}, Col {col}:  {lineText}" },
            };
            tint.Add(border);

            var span = BuildSpan(fc.FilePath, fc.FileText, start, length);
            var leaf = new TreeViewItem { Header = border };
            leaf.Tapped += (_, e) => { e.Handled = true; if (_navigate is { } nav) _ = nav(span); };
            fileItem.Items.Add(leaf);
        }
        parent.Items.Add(fileItem);
    }

    /// <summary>Adds a collapsed group node (file children) for an opt-in change set; returns its hit count.</summary>
    private int AddGroup(TreeView tree, IReadOnlyList<RenameFileChanges> group, string label, List<Border> tint)
    {
        int count = group.Sum(fc => fc.Hits.Count);
        if (count == 0) return 0;

        var node = new TreeViewItem { Header = $"{label}  ({count})", IsExpanded = false };
        foreach (var fc in group) AddFileNode(node, fc, tint);
        tree.Items.Add(node);
        return count;
    }

    // A checkbox + a "?" help button whose flyout explains the option (keeps the label short).
    private static Control MakeCheckRow(CheckBox check, bool visible, string help)
    {
        check.VerticalAlignment = VerticalAlignment.Center;
        var helpBtn = new Button
        {
            Content = "?",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = new Flyout
            {
                Content = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
            },
        };
        ToolTip.SetTip(helpBtn, "What does this do?");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            IsVisible = visible,
            Children = { check, helpBtn },
        };
    }

    private static void SetTint(List<Border> borders, bool on)
    {
        var brush = on ? HighlightBrush : Brushes.Transparent;
        foreach (var b in borders) b.Background = brush;
    }

    private static SourceSpan BuildSpan(string filePath, string fileText, int start, int length)
    {
        var (line, col) = OffsetToLineCol(fileText, start);
        var (eline, ecol) = OffsetToLineCol(fileText, start + length);
        return new SourceSpan(filePath, new SourceLocation(line, col), new SourceLocation(eline, ecol), start, length);
    }

    private static string BuildSummary(IReadOnlyList<RenameFileChanges> changes, string oldName, string newName)
    {
        int total = 0;
        foreach (var fc in changes) total += fc.Hits.Count;
        return $"Rename '{oldName}' → '{newName}': {total} occurrence{(total == 1 ? "" : "s")} across {changes.Count} file{(changes.Count == 1 ? "" : "s")}. Click an entry to jump to it.";
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
