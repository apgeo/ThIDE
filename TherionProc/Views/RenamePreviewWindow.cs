// Preview window for symbol rename (#1). Shows a collapsible tree of all proposed text changes
// before applying; returns true on Apply, false on Cancel. Three opt-in expansions are offered:
//   1. rename equate-linked same-named stations in other surveys (keep the equated point connected),
//   2. rename every same-named station/survey elsewhere (≈ replace-all),
//   3. rename inside comments.
//
// Two layouts are offered (a selector at the top switches live, preserving the selection) so the two
// designs can be compared:
//   • "Checkboxes below" — the classic: three checkboxes under the list, each with a "?" help flyout;
//     checking one tints its whole section and reveals it.
//   • "Checkable list"   — the proposed: a tri-state checkbox on each section header plus a checkbox on
//     every entry, so occurrences can be cherry-picked. The "?" help lives on the section headers.
// In both, entries that will be replaced are tinted pale blue (base changes always), and clicking an
// entry's text navigates to its file/line without closing the window.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Therion.Core;
using TherionProc.Resources;

namespace TherionProc.Views;

internal sealed class RenamePreviewWindow : Window
{
    private enum PreviewLayout { ChecksBelow, CheckableList }

    /// <summary>Rename the stations an <c>equate</c> links to this one that share its name (other surveys).</summary>
    public bool IncludeEquateLinked => _equate.Included;
    /// <summary>Rename every other same-named symbol regardless of survey/file (≈ replace-all).</summary>
    public bool IncludeSameName => _same.Included;
    /// <summary>Rename occurrences inside <c>#</c> comments.</summary>
    public bool IncludeComments => _comment.Included;

    // Pale blue tint (translucent so it reads on both light and dark themes) for "will be replaced".
    // Blue is reserved for that meaning — headers are made distinct with weight + a rule, not colour.
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x21, 0x96, 0xF3));
    private static readonly IBrush SectionRule   = new SolidColorBrush(Color.FromArgb(0x66, 0x90, 0x90, 0x90));
    private static readonly IBrush DimText       = new SolidColorBrush(Color.FromArgb(0x99, 0x88, 0x88, 0x88));

    private readonly Func<SourceSpan, Task>? _navigate;
    private readonly IReadOnlyList<RenameFileChanges> _changes;
    private readonly OptionGroup _equate, _same, _comment;
    private readonly OptionGroup[] _groups;
    private readonly string _oldName;
    private readonly bool _focusName;
    private TextBox _nameBox = null!;
    private Button _applyBtn = null!;

    /// <summary>The new name the user typed (trimmed). The window carries its own name field so the
    /// complex-rename path can start straight here without a separate input dialog.</summary>
    public string ResultNewName => (_nameBox.Text ?? string.Empty).Trim();

    // The "Checkboxes below" layout is retained in source but DISABLED from the UI (the checkable list
    // won the comparison for now). Kept behind this fixed value — and reachable only if flipped here — for
    // testing / a possible future redesign; remove the ChecksBelow branches when the decision is final.
    private readonly PreviewLayout _layout = PreviewLayout.CheckableList;
    private bool _suppress;                     // guards programmatic checkbox updates from re-entering handlers
    private bool _opened;                        // window shown → safe to auto-grow to fit
    private double _maxHeight = 900;             // clamp for auto-grow (set from the screen on open)
    private double _scaling = 1;                 // screen scaling (physical px per DIP)
    private PixelRect _workingArea;              // screen working area (physical px) for centring/clamping
    private readonly Border _treeHost = new();
    private readonly Border _checksHost = new();
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
        string initialName,
        bool focusNameField = true,
        Func<SourceSpan, Task>? navigate = null)
    {
        _navigate = navigate;
        _changes = changes;
        _oldName = oldName;
        _focusName = focusNameField;
        _equate  = new OptionGroup("Equate-linked stations (same name, other surveys)", equateLinkedChanges, EquateHelp);
        _same    = new OptionGroup("Same-named symbols in other surveys / files", sameNameChanges, SameNameHelp);
        _comment = new OptionGroup("In comments", commentChanges, CommentsHelp);
        _groups  = new[] { _equate, _same, _comment };

        Width = 600;
        Height = 540;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Opened += OnOpened;

        // Name field at the top (the complex-rename path opens straight here with no separate input dialog).
        var nameLabel = new TextBlock
        {
            Text = string.Format(Tr.Get("Rename_NewNameFor"), oldName),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _nameBox = new TextBox { Text = initialName };
        _nameBox.TextChanged += (_, _) => UpdateNameDependent();
        var nameRow = new DockPanel();
        DockPanel.SetDock(nameLabel, Avalonia.Controls.Dock.Left);
        nameRow.Children.Add(nameLabel);
        nameRow.Children.Add(_nameBox);

        var summary = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = DimText,
            Text = BuildSummary(changes),
        };
        var topBar = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8), Children = { nameRow, summary } };

        _applyBtn     = new Button { Content = Tr.Get("Common_Apply"),  IsDefault = true, MinWidth = 80 };
        var cancelBtn = new Button { Content = Tr.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        _applyBtn.Click += (_, _) => Close(true);
        cancelBtn.Click += (_, _) => Close(false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelBtn, _applyBtn },
        };

        _checksHost.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(topBar,  Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_checksHost, Avalonia.Controls.Dock.Bottom);

        Content = new DockPanel
        {
            Margin = new Thickness(12),
            LastChildFill = true,
            Children = { topBar, buttons, _checksHost, _treeHost },
        };

        UpdateNameDependent();
        Rebuild();
    }

    // Keep the title and the Apply button's enabled state in sync with the typed name.
    private void UpdateNameDependent()
    {
        var nn = ResultNewName;
        Title = string.Format(Tr.Get("Rename_TitleFormat"), _oldName, nn);
        if (_applyBtn is not null) _applyBtn.IsEnabled = nn.Length > 0 && nn != _oldName;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (Screens is { } screens && (screens.ScreenFromWindow(this) ?? screens.Primary) is { Scaling: > 0 } s)
        {
            _scaling = s.Scaling;
            _workingArea = s.WorkingArea;
            _maxHeight = _workingArea.Height / _scaling * 0.8;
        }
        _opened = true;
        if (_focusName) { _nameBox.Focus(); _nameBox.SelectAll(); }
        GrowToFit();
    }

    // Grow the window (never shrink, capped at 80% of the screen) so expanded sections stay visible
    // without scrolling — the list can need much more room once the selected sections are unfolded.
    private void GrowToFit()
    {
        if (!_opened) return;
        int rows = 0;
        foreach (var fc in _changes) rows += 1 + fc.Hits.Count;   // base is always expanded
        foreach (var g in _groups)
        {
            if (g.Count == 0) continue;
            rows += 1;                                            // section header
            if (g.Node is { IsExpanded: true })
                foreach (var fc in g.Source) rows += 1 + fc.Hits.Count;
        }
        const double rowHeight = 22, chrome = 200;                // top bar + checks + buttons + margins
        double target = Math.Min(_maxHeight, rows * rowHeight + chrome);
        if (target > Height) { Height = target; RecenterVertically(); }
    }

    // Re-centre the (now taller) window vertically on its screen, clamped so the bottom never runs off it.
    private void RecenterVertically()
    {
        if (_workingArea.Height <= 0 || _scaling <= 0) return;
        int physicalHeight = (int)(Height * _scaling);
        int y = _workingArea.Y + (_workingArea.Height - physicalHeight) / 2;
        y = Math.Clamp(y, _workingArea.Y, _workingArea.Y + Math.Max(0, _workingArea.Height - physicalHeight));
        Position = new PixelPoint(Position.X, y);
    }

    /// <summary>The optional changes to actually apply, given the current selection (both layouts).</summary>
    public List<RenameFileChanges> AppliedOptionalChanges()
    {
        var result = new List<RenameFileChanges>();
        foreach (var g in _groups)
        {
            if (!g.Included) continue;
            foreach (var fc in g.Source)
            {
                var hits = fc.Hits.Where(h => !g.Excluded.Contains((fc.FilePath, h.Start))).ToList();
                if (hits.Count > 0) result.Add(fc with { Hits = hits });
            }
        }
        return result;
    }

    // ---- layout build ----------------------------------------------------

    private void Rebuild()
    {
        _baseFileNodes.Clear();
        _sectionNodes.Clear();

        var tree = new TreeView { Margin = new Thickness(0, 0, 0, 0) };

        // Base changes — always applied, so always tinted, never checkable.
        var baseTint = new List<Border>();
        foreach (var fc in _changes) _baseFileNodes.Add(AddFileNode(tree, fc, baseTint, group: null));
        SetTint(baseTint, true);

        foreach (var g in _groups)
        {
            g.Reset();
            if (g.Count > 0) AddOptionGroup(tree, g);
        }
        _treeHost.Child = tree;

        if (_layout == PreviewLayout.ChecksBelow)
        {
            _checksHost.Child = BuildBottomChecks();
            _checksHost.IsVisible = _groups.Any(g => g.Count > 0);
        }
        else
        {
            _checksHost.Child = null;
            _checksHost.IsVisible = false;
        }

        GrowToFit();
    }

    private StackPanel BuildBottomChecks()
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var g in _groups)
        {
            if (g.Count == 0) continue;
            var check = new CheckBox
            {
                Content = BottomCheckLabel(g),
                IsChecked = g.Included,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var group = g;
            check.IsCheckedChanged += (_, _) =>
            {
                if (_suppress) return;
                group.SetAll(check.IsChecked == true);
                SetTint(group.Tint, group.Included);
                if (group.Included) Reveal(group.Node);
                else if (group.Node is { } n) n.IsExpanded = false;
            };
            panel.Children.Add(MakeCheckRow(check, g.Help));
        }
        return panel;
    }

    private string BottomCheckLabel(OptionGroup g)
    {
        string s = g.Count == 1 ? "" : "s";
        if (g == _equate)  return $"Also rename {g.Count} equate-linked station{s} (same name, other surveys)";
        if (g == _same)    return $"Also rename {g.Count} same-named occurrence{s} in other surveys / files";
        return $"Also rename {g.Count} occurrence{s} in comments";
    }

    private void AddOptionGroup(TreeView tree, OptionGroup g)
    {
        bool checkable = _layout == PreviewLayout.CheckableList;

        Control headerContent;
        if (checkable)
        {
            var groupCheck = new CheckBox
            {
                IsThreeState = true,
                VerticalAlignment = VerticalAlignment.Center,
                Content = SectionHeaderText(g.Label, g.Count),
            };
            IsolatePointer(groupCheck);   // keep clicks from reaching the TreeViewItem (select/expand)
            g.GroupCheck = groupCheck;
            var group = g;
            groupCheck.IsCheckedChanged += (_, _) =>
            {
                if (_suppress) return;
                // The header box is tri-state only to *display* "some selected"; a user click must plainly
                // toggle the whole group (a three-state control would otherwise cycle into indeterminate and
                // never uncheck). Decide from the model's pre-click state, not the control's new value.
                bool wasAllIncluded = group.Excluded.Count == 0;
                group.SetAll(!wasAllIncluded);
                RefreshGroupVisuals(group);   // re-stamps the tri-state box (suppressed) to match
                GrowToFit();
            };
            headerContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { groupCheck, HelpButton(g.Help) } };
        }
        else
        {
            headerContent = SectionHeaderText(g.Label, g.Count);
        }

        var headerBorder = new Border
        {
            Padding = new Thickness(3, 2),
            CornerRadius = new CornerRadius(2),
            BorderBrush = SectionRule,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headerContent,
        };
        g.HeaderBorder = headerBorder;
        g.Tint.Add(headerBorder);

        var node = new TreeViewItem { Header = headerBorder, IsExpanded = checkable && g.Included };
        node.PropertyChanged += (_, e) => { if (e.Property == TreeViewItem.IsExpandedProperty) GrowToFit(); };
        g.Node = node;
        foreach (var fc in g.Source) AddFileNode(node, fc, g.Tint, group: g);
        tree.Items.Add(node);
        _sectionNodes.Add(node);

        if (checkable) RefreshGroupVisuals(g);
        else SetTint(g.Tint, g.Included);
    }

    /// <summary>Builds a file node with one leaf per hit; leaves navigate on click, are tint-tracked, and
    /// (in the checkable layout, for an optional group) carry a per-entry checkbox.</summary>
    private TreeViewItem AddFileNode(ItemsControl parent, RenameFileChanges fc, List<Border> tint, OptionGroup? group)
    {
        bool checkable = _layout == PreviewLayout.CheckableList && group is not null;

        var fileHeader = new Border
        {
            Padding = new Thickness(3, 0),
            CornerRadius = new CornerRadius(2),
            Child = new TextBlock { Text = $"{Path.GetFileName(fc.FilePath)}  ({fc.Hits.Count} change{(fc.Hits.Count == 1 ? "" : "s")})", FontWeight = FontWeight.SemiBold },
        };
        tint.Add(fileHeader);
        var fileItem = new TreeViewItem { Header = fileHeader, IsExpanded = true };
        ToolTip.SetTip(fileItem, fc.FilePath);

        foreach (var (start, length) in fc.Hits)
        {
            var (line, col) = OffsetToLineCol(fc.FileText, start);
            var lineText = GetLineText(fc.FileText, start).Trim();
            var span = BuildSpan(fc.FilePath, fc.FileText, start, length);

            var text = EntryText(line, col, lineText);

            Control content = text;
            var key = (fc.FilePath, start);
            CheckBox? leafCheck = null;
            if (checkable)
            {
                leafCheck = new CheckBox { IsChecked = !group!.Excluded.Contains(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                IsolatePointer(leafCheck);   // let the checkbox toggle reliably, not the row's selection
                content = new StackPanel { Orientation = Orientation.Horizontal, Children = { leafCheck, text } };
            }

            var border = new Border { Padding = new Thickness(3, 0), CornerRadius = new CornerRadius(2), Child = content };
            tint.Add(border);

            if (checkable)
            {
                var g = group!;
                var b = border;
                var lc = leafCheck!;
                g.Leaves.Add((lc, b, fc.FilePath, start));
                lc.IsCheckedChanged += (_, _) =>
                {
                    if (_suppress) return;
                    bool on = lc.IsChecked == true;
                    if (on) g.Excluded.Remove(key); else g.Excluded.Add(key);
                    TintBorder(b, on);
                    g.RecomputeIncluded();
                    RefreshGroupCheckState(g);
                };
                TintBorder(border, lc.IsChecked == true);
            }

            // Double-click the row to jump to that file/line (keeps this window open).
            var leaf = new TreeViewItem { Header = border };
            leaf.DoubleTapped += (_, e) => { e.Handled = true; if (_navigate is { } nav) _ = nav(span); };
            fileItem.Items.Add(leaf);
        }
        parent.Items.Add(fileItem);
        return fileItem;
    }

    // Push a group's model state onto its widgets (leaf boxes + tints + header tri-state).
    private void RefreshGroupVisuals(OptionGroup g)
    {
        _suppress = true;
        foreach (var (box, border, file, start) in g.Leaves)
        {
            bool on = !g.Excluded.Contains((file, start));
            box.IsChecked = on;
            TintBorder(border, on);
        }
        _suppress = false;
        RefreshGroupCheckState(g);
    }

    // Header tri-state + header tint from the leaf selection.
    private void RefreshGroupCheckState(OptionGroup g)
    {
        _suppress = true;
        if (g.GroupCheck is { } gc)
            gc.IsChecked = g.Excluded.Count == 0 ? true : g.Excluded.Count >= g.Count ? false : (bool?)null;
        _suppress = false;
        if (g.HeaderBorder is { } hb) TintBorder(hb, g.Included);
    }

    private void Reveal(TreeViewItem? target)
    {
        if (target is null) return;
        foreach (var b in _baseFileNodes) b.IsExpanded = false;
        foreach (var s in _sectionNodes) s.IsExpanded = ReferenceEquals(s, target);
        target.BringIntoView();
    }

    // ---- small builders --------------------------------------------------

    private static Control MakeCheckRow(CheckBox check, string help)
        => new StackPanel { Orientation = Orientation.Horizontal, Children = { check, HelpButton(help) } };

    // A section header: bold label + a dim count (distinct from rows by weight + a rule, not colour —
    // blue is reserved for the "will be replaced" tint).
    private static TextBlock SectionHeaderText(string label, int count)
    {
        var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        tb.Inlines!.Add(new Run(label) { FontWeight = FontWeight.Bold });
        tb.Inlines.Add(new Run($"   ({count})") { Foreground = DimText });
        return tb;
    }

    // A change entry: a dim, smaller "line/col" prefix followed by the actual source line (emphasised).
    private static TextBlock EntryText(int line, int col, string lineText)
    {
        var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        tb.Inlines!.Add(new Run($"L{line}:C{col}   ") { Foreground = DimText, FontSize = 11 });
        tb.Inlines.Add(new Run(lineText));
        return tb;
    }

    // Stop a control's pointer-press from bubbling to the TreeViewItem, so clicking a checkbox toggles it
    // reliably (and repeatedly) instead of the row swallowing the click for selection / expand-collapse.
    private static void IsolatePointer(Control c) =>
        c.AddHandler(InputElement.PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);

    private static Button HelpButton(string help)
    {
        var btn = new Button
        {
            Content = "?",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Flyout = new Flyout { Content = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 } },
        };
        ToolTip.SetTip(btn, "What does this do?");
        return btn;
    }

    private static void SetTint(List<Border> borders, bool on)
    {
        var brush = on ? HighlightBrush : Brushes.Transparent;
        foreach (var b in borders) b.Background = brush;
    }

    private static void TintBorder(Border b, bool on) => b.Background = on ? HighlightBrush : Brushes.Transparent;

    private static SourceSpan BuildSpan(string filePath, string fileText, int start, int length)
    {
        var (line, col) = OffsetToLineCol(fileText, start);
        var (eline, ecol) = OffsetToLineCol(fileText, start + length);
        return new SourceSpan(filePath, new SourceLocation(line, col), new SourceLocation(eline, ecol), start, length);
    }

    private static string BuildSummary(IReadOnlyList<RenameFileChanges> changes)
    {
        int total = 0;
        foreach (var fc in changes) total += fc.Hits.Count;
        return string.Format(Tr.Get("Rename_PreviewSummary"), total, changes.Count);
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

    // ---- model -----------------------------------------------------------

    /// <summary>One opt-in change set + its selection state (whole-group include + per-entry exclusions).</summary>
    private sealed class OptionGroup
    {
        public OptionGroup(string label, IReadOnlyList<RenameFileChanges> source, string help)
        {
            Label = label; Source = source; Help = help;
            Count = source.Sum(fc => fc.Hits.Count);
            // Default: nothing selected (every entry excluded, group not included) — opt-in.
            foreach (var fc in source)
                foreach (var (start, _) in fc.Hits)
                    Excluded.Add((fc.FilePath, start));
        }

        public string Label { get; }
        public IReadOnlyList<RenameFileChanges> Source { get; }
        public string Help { get; }
        public int Count { get; }

        public bool Included { get; private set; }
        public HashSet<(string File, int Start)> Excluded { get; } = new();

        // rebuilt per layout:
        public List<Border> Tint = new();
        public TreeViewItem? Node;
        public Border? HeaderBorder;
        public CheckBox? GroupCheck;
        public List<(CheckBox Box, Border Border, string File, int Start)> Leaves = new();

        // Clear only the per-build widget references (selection state persists across layout switches).
        public void Reset()
        {
            Tint = new();
            Node = null;
            HeaderBorder = null;
            GroupCheck = null;
            Leaves = new();
        }

        public void SetAll(bool on)
        {
            Excluded.Clear();
            if (!on)
                foreach (var fc in Source)
                    foreach (var (start, _) in fc.Hits)
                        Excluded.Add((fc.FilePath, start));
            Included = on;
        }

        public void RecomputeIncluded() => Included = Excluded.Count < Count;
    }
}
