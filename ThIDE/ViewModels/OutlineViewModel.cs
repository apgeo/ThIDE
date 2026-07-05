// document outline / symbol tree. Builds a live nested tree of the active document's
// block structure (survey → centreline → scrap → line/area, plus map/group/surface/layout) from
// its text, with click-to-navigate and a filter box. Tracks the active document via IDocumentService.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Core;
using ThIDE.Editor;
using ThIDE.Services;

namespace ThIDE.ViewModels;

/// <summary>One node in the document outline tree (a block: survey, centreline, scrap, …).</summary>
public sealed class OutlineNode
{
    public string Title { get; }
    public string Kind { get; }   // canonical block keyword (drives the glyph)
    public int Line { get; }      // 1-based line of the opener
    public string Glyph { get; }
    public ObservableCollection<OutlineNode> Children { get; } = new();

    public OutlineNode(string title, string kind, int line)
    {
        Title = title;
        Kind = kind;
        Line = line;
        Glyph = GlyphFor(kind);
    }

    private static string GlyphFor(string kind) => kind switch
    {
        "survey"     => "▣",
        "centreline" => "↘",
        "scrap"      => "◇",
        "map"        => "▦",
        "line"       => "／",
        "area"       => "▨",
        "surface"    => "≋",
        "group"      => "❏",
        "layout"     => "⚙",
        "station"    => "∘",
        _            => "•",
    };
}

public sealed partial class OutlineViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;
    private string? _currentFile;

    public ObservableCollection<OutlineNode> Roots { get; } = new();

    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    /// <summary>#1: also list survey stations under each centreline (off by default — verbose).</summary>
    [ObservableProperty] private bool _includeStations;

    /// <summary>Two-way bound to the tree's selection; navigation happens on double-click, not select.</summary>
    [ObservableProperty] private OutlineNode? _selectedNode;

    public OutlineViewModel() { } // design-time

    public OutlineViewModel(IDocumentService documents)
    {
        _documents = documents;
        // DocumentChanged alone is enough: SetActive always raises it right after
        // ActiveDocumentChanged, so subscribing to both rebuilt the outline twice per tab switch.
        _documents.DocumentChanged += (_, _) => Rebuild();
        Rebuild();
    }

    partial void OnFilterChanged(string value) => Rebuild();
    partial void OnIncludeStationsChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        Roots.Clear();
        var doc = _documents?.Active;
        _currentFile = doc?.FilePath;
        if (doc is null || string.IsNullOrEmpty(doc.DocumentText)) { IsEmpty = true; return; }

        var tree = BuildTree(doc.DocumentText, IncludeStations);
        if (!string.IsNullOrWhiteSpace(Filter)) tree = Prune(tree, Filter.Trim());
        foreach (var n in tree) Roots.Add(n);
        IsEmpty = Roots.Count == 0;
    }

    // Centreline sub-commands that are NOT survey-data rows (so the rest are treated as shots).
    private static readonly HashSet<string> CentrelineKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "data", "units", "calibrate", "sd", "grade", "declination", "instrument", "infer", "team",
        "date", "explo-date", "explo-team", "flags", "station", "fix", "equate", "extend", "break",
        "group", "endgroup", "mark", "walls", "vthreshold", "cs", "copyright", "attr", "count",
        "station-names", "centreline", "centerline", "endcentreline", "endcenterline",
    };
    private static readonly string[] DefaultDataCols = { "from", "to", "length", "compass", "clino" };

    internal static List<OutlineNode> BuildTree(string text, bool includeStations)
    {
        var roots = new List<OutlineNode>();
        var stack = new Stack<OutlineNode>();

        // Station context for the innermost centreline (centrelines are not normally nested).
        OutlineNode? centreline = null;
        string[] cols = DefaultDataCols;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int lineNo = 0;
        foreach (var line in SplitLines(text))
        {
            lineNo++;
            var fw = TherionBlocks.FirstWord(line);
            if (fw.Length == 0) continue;

            if (TherionBlocks.IsBlockOpenerLine(line, out var type))
            {
                var node = new OutlineNode(LabelFor(type, line), type, lineNo);
                if (stack.Count > 0) stack.Peek().Children.Add(node);
                else roots.Add(node);
                stack.Push(node);
                if (type == "centreline") { centreline = node; cols = DefaultDataCols; seen.Clear(); }
            }
            else if (TherionBlocks.CloserType(fw) is { } ctype)
            {
                while (stack.Count > 0 && stack.Peek().Kind != ctype) stack.Pop();
                if (stack.Count > 0) stack.Pop();
                if (ctype == "centreline") centreline = null;
            }
            else if (includeStations && centreline is not null && !fw.StartsWith('#'))
            {
                AddStationsFromLine(line, fw, lineNo, centreline, seen, ref cols);
            }
        }
        return roots;
    }

    /// <summary>Extracts station names from a centreline line (data row, or station/fix/equate) (#1).</summary>
    private static void AddStationsFromLine(string line, string firstWord, int lineNo,
        OutlineNode centreline, HashSet<string> seen, ref string[] cols)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var fw = firstWord.ToLowerInvariant();

        if (fw == "data")
        {
            // "data <style> col1 col2 …" — record the column order (stations are from/to/station).
            if (tokens.Length >= 3) cols = tokens[2..].Select(t => t.ToLowerInvariant()).ToArray();
            return;
        }
        if (fw is "station" or "fix") { if (tokens.Length >= 2) Add(tokens[1]); return; }
        if (fw == "equate") { for (int i = 1; i < tokens.Length; i++) Add(tokens[i]); return; }
        if (CentrelineKeywords.Contains(fw)) return;   // another sub-command, not a shot

        // A survey-data row: pick the tokens at the station columns of the active data format.
        for (int i = 0; i < cols.Length && i < tokens.Length; i++)
            if (cols[i] is "from" or "to" or "station") Add(tokens[i]);

        void Add(string raw)
        {
            var name = raw.Trim();
            if (name.Length == 0 || name == "-" || name == "." || !IsStationToken(name)) return;
            if (seen.Add(name)) centreline.Children.Add(new OutlineNode(name, "station", lineNo));
        }
    }

    private static bool IsStationToken(string s)
    {
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '@')) return false;
        return true;
    }

    /// <summary>Navigates the editor to the node's source line (double-click from the tree, #1).</summary>
    public void NavigateToNode(OutlineNode? node)
    {
        if (node is null || _documents is null || string.IsNullOrEmpty(_currentFile)) return;
        var loc = new SourceLocation(node.Line, 1);
        // Length must be > 0: NavigateToSpanAsync and ScrollTo both ignore IsEmpty (zero-length)
        // spans, which is why the double-click previously did nothing. Start.Line drives the jump.
        _ = _documents.NavigateToSpanAsync(new SourceSpan(_currentFile!, loc, loc, 0, 1));
    }

    private static List<OutlineNode> Prune(List<OutlineNode> nodes, string filter)
    {
        var result = new List<OutlineNode>();
        foreach (var n in nodes)
        {
            var keptChildren = Prune(n.Children.ToList(), filter);
            bool selfMatch = n.Title.Contains(filter, StringComparison.OrdinalIgnoreCase);
            if (selfMatch || keptChildren.Count > 0)
            {
                var clone = new OutlineNode(n.Title, n.Kind, n.Line);
                foreach (var c in keptChildren) clone.Children.Add(c);
                result.Add(clone);
            }
        }
        return result;
    }

    private static string LabelFor(string type, string line)
    {
        var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && !parts[1].StartsWith('-')
            ? $"{type} {parts[1]}"
            : type;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') { yield return text.Substring(start, i - start); start = i + 1; }
        }
        yield return text.Substring(start);
    }
}
