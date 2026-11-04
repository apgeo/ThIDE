using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class EditPreviewItem(EditPreviewRow row, string location) : ObservableObject
{
    public EditPreviewRow Row { get; } = row;
    public string Location { get; } = location;
    /// <summary>What a whole-file change does ("1 line: trailing spaces removed"), or null.</summary>
    public string? Description => Row.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Row.Description);
    // A reformat changes whitespace: draw it, or "before" and "after" look the same.
    public string Before => HasDescription ? VisibleWhitespace(Row.Before) : Row.Before;
    public string After => HasDescription ? VisibleWhitespace(Row.After) : Row.After;
    /// <summary>False for a description whose line looks the same before and after (only its line end changed).</summary>
    public bool ShowsLines => !HasDescription || Row.Before != Row.After;
    [ObservableProperty] private bool _isChecked = true;

    /// <summary>Tabs as →, and spaces as · where they are leading or trailing.</summary>
    public static string VisibleWhitespace(string line)
    {
        var body = line.Trim(' ', '\t');
        if (body.Length == 0) return Mark(line);
        int lead = line.Length - line.TrimStart(' ', '\t').Length;
        int trail = line.Length - line.TrimEnd(' ', '\t').Length;
        return Mark(line[..lead]) + body.Replace('\t', '→') + Mark(line[(line.Length - trail)..]);
    }

    private static string Mark(string whitespace) => whitespace.Replace(' ', '·').Replace('\t', '→');
}

/// <summary>A list of proposed edits to tick before applying (fix all, sync maps, format all).</summary>
public sealed partial class EditPreviewViewModel : ObservableObject
{
    private readonly EditBatch _batch;

    /// <param name="allOrNothing">The edits only make sense together (a renumbering): no unticking.</param>
    public EditPreviewViewModel(string title, string summary, EditBatch batch, IReadOnlyList<EditPreviewRow> rows, string? root, bool allOrNothing = false)
    {
        Title = title;
        CanChoose = !allOrNothing;
        Summary = summary;
        _batch = batch;
        foreach (var r in rows)
        {
            var file = root is null ? Path.GetFileName(r.FilePath) : Path.GetRelativePath(root, r.FilePath).Replace('\\', '/');
            var item = new EditPreviewItem(r, $"{file}:{r.Line}");
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CheckedCount));
            Items.Add(item);
        }
    }

    public string Title { get; }
    public string Summary { get; }
    public bool CanChoose { get; }
    public ObservableCollection<EditPreviewItem> Items { get; } = new();
    public int CheckedCount => Items.Count(i => i.IsChecked);
    /// <summary>Nothing to change: the summary says so, and the only button is Close.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>A reformat in words: "1 line: trailing spaces removed", "3 lines: indentation changed, …".</summary>
    public static string DescribeFormatChange(FormatChange change)
    {
        var kinds = new List<string>();
        void Add(FormatChangeKinds kind, string key) { if (change.Kinds.HasFlag(kind)) kinds.Add(Tr.Get(key)); }
        Add(FormatChangeKinds.Indentation, "FormatAll_KindIndent");
        Add(FormatChangeKinds.TrailingWhitespace, "FormatAll_KindTrailing");
        Add(FormatChangeKinds.Encoding, "FormatAll_KindEncoding");
        Add(FormatChangeKinds.Reordered, "FormatAll_KindReordered");
        Add(FormatChangeKinds.Other, "FormatAll_KindOther");
        Add(FormatChangeKinds.LineEndings, "FormatAll_KindEndings");
        var what = string.Join(", ", kinds);
        if (change.Lines == 0) return what;
        var lines = Tr.Plural("FormatAll_Lines", change.Lines);
        return what.Length == 0 ? lines : lines + ": " + what;
    }

    /// <summary>The summary of an empty plan: "Nothing to change in 91 files.", plus the files left unread.</summary>
    public static string NothingToChange(int checkedFiles, int skipped)
    {
        var text = Tr.Plural("FormatAll_Nothing", Math.Max(0, checkedFiles));
        return skipped > 0 ? text + " " + Tr.Plural("FormatAll_NothingSkipped", skipped) : text;
    }

    /// <summary>
    /// The summary of a plan with changes, <paramref name="key"/> holding the rest of the text:
    /// "1 file would change. 2 files are left alone because … Untick a file to leave it out."
    /// </summary>
    public static string ChangeSummary(string key, int changing, int skipped)
    {
        var counts = Tr.Plural("Edit_WouldChange", changing);
        if (skipped > 0) counts += " " + Tr.Plural("Edit_LeftAlone", skipped);
        return string.Format(Tr.Get(key), counts);
    }

    /// <summary>The batch with only the ticked edits.</summary>
    public EditBatch Chosen() => FixAll.Keep(_batch, Items.Where(i => i.IsChecked).Select(i => i.Row));
}
