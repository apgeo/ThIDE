// Preview window for symbol rename (#1). Shows a collapsible tree of all proposed text changes
// (section header → file nodes → change entries) before applying. Returns true when the user clicks
// Apply, false on Cancel. Three opt-in expansions are offered, each a checkbox + a "?" help flyout:
//   1. rename equate-linked same-named stations in other surveys (keep the equated point connected),
//   2. rename every same-named station/survey elsewhere (≈ replace-all),
//   3. rename inside comments.
// Entries + their section/file headers that will be replaced under the current checkbox combination are
// tinted pale blue (the base changes always are). Checking a section reveals it (collapses the base rows
// so its now-blue header is in view). Clicking an entry navigates to its file/line, keeping this window open.

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
    private static readonly IBrush SectionText   = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly IBrush SectionRule   = new SolidColorBrush(Color.FromArgb(0x66, 0x90, 0x90, 0x90));

    private readonly Func<SourceSpan, Task>? _navigate;
    private readonly List<TreeViewItem> _baseFileNodes = new();
    private readonly List<TreeViewItem> _sectionNodes = new();

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
        foreach (var fc in changes) _baseFileNodes.Add(AddFileNode(tree, fc, baseTint));
        SetTint(baseTint, true);

        var equate  = AddGroup(tree, equateLinkedChanges, "Equate-linked stations (same name, other surveys)");
        var same    = AddGroup(tree, sameNameChanges, "Same-named symbols in other surveys / files");
        var comment = AddGroup(tree, commentChanges, "In comments");

        // ---- opt-in checkboxes (equate-linked first, then replace-all, then comments) ----
        var equateCheck = new CheckBox
        {
            Content = $"Also rename {equate.Count} equate-linked station{(equate.Count == 1 ? "" : "s")} (same name, other surveys)",
        };
        equateCheck.IsCheckedChanged += (_, _) => Toggle(equate, IncludeEquateLinked = equateCheck.IsChecked == true);

        var sameCheck = new CheckBox
        {
            Content = $"Also rename {same.Count} same-named occurrence{(same.Count == 1 ? "" : "s")} in other surveys / files",
        };
        sameCheck.IsCheckedChanged += (_, _) => Toggle(same, IncludeSameName = sameCheck.IsChecked == true);

        var commentCheck = new CheckBox
        {
            Content = $"Also rename {comment.Count} occurrence{(comment.Count == 1 ? "" : "s")} in comments",
        };
        commentCheck.IsCheckedChanged += (_, _) => Toggle(comment, IncludeComments = commentCheck.IsChecked == true);

        var checks = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                MakeCheckRow(equateCheck, equate.Count > 0, EquateHelp),
                MakeCheckRow(sameCheck, same.Count > 0, SameNameHelp),
                MakeCheckRow(commentCheck, comment.Count > 0, CommentsHelp),
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

    private sealed record Section(TreeViewItem? Node, List<Border> Tint, int Count);

    // Tint a whole section (header + files + entries) and, when turning on, reveal it: collapse the base
    // rows (the bulky "top ones") and bring the now-blue section header into view.
    private void Toggle(Section section, bool on)
    {
        SetTint(section.Tint, on);
        if (section.Node is not { } node) return;
        if (on)
        {
            foreach (var b in _baseFileNodes) b.IsExpanded = false;
            foreach (var s in _sectionNodes) s.IsExpanded = ReferenceEquals(s, node);
            node.BringIntoView();
        }
        else node.IsExpanded = false;
    }

    /// <summary>Builds a file node with one leaf per hit; leaves navigate on click and are tint-tracked.</summary>
    private TreeViewItem AddFileNode(ItemsControl parent, RenameFileChanges fc, List<Border> tint)
    {
        var header = MakeHeaderBorder($"{Path.GetFileName(fc.FilePath)}  ({fc.Hits.Count} change{(fc.Hits.Count == 1 ? "" : "s")})",
            FontWeight.SemiBold, null, null);
        tint.Add(header);
        var fileItem = new TreeViewItem { Header = header, IsExpanded = true };
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
        return fileItem;
    }

    /// <summary>Adds a collapsed, visually-distinct section node for an opt-in change set.</summary>
    private Section AddGroup(TreeView tree, IReadOnlyList<RenameFileChanges> group, string label)
    {
        var tint = new List<Border>();
        int count = group.Sum(fc => fc.Hits.Count);
        if (count == 0) return new Section(null, tint, 0);

        var header = MakeHeaderBorder($"{label}   ({count})", FontWeight.Bold, SectionText, SectionRule);
        tint.Add(header);
        var node = new TreeViewItem { Header = header, IsExpanded = false };
        foreach (var fc in group) AddFileNode(node, fc, tint);
        tree.Items.Add(node);
        _sectionNodes.Add(node);
        return new Section(node, tint, count);
    }

    // A tintable header row: bold (+ optional accent colour and a bottom rule for section headers).
    private static Border MakeHeaderBorder(string text, FontWeight weight, IBrush? foreground, IBrush? rule)
    {
        var tb = new TextBlock { Text = text, FontWeight = weight };
        if (foreground is not null) tb.Foreground = foreground;
        return new Border
        {
            Padding = new Thickness(3, rule is null ? 0 : 2),
            CornerRadius = new CornerRadius(2),
            BorderBrush = rule,
            BorderThickness = rule is null ? default : new Thickness(0, 0, 0, 1),
            Child = tb,
        };
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
