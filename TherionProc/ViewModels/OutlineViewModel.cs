// EDIT-09 — document outline / symbol tree. Builds a live nested tree of the active document's
// block structure (survey → centreline → scrap → line/area, plus map/group/surface/layout) from
// its text, with click-to-navigate and a filter box. Tracks the active document via IDocumentService.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Core;
using TherionProc.Editor;
using TherionProc.Services;

namespace TherionProc.ViewModels;

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
        _            => "•",
    };
}

public sealed partial class OutlineViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;
    private string? _currentFile;
    private bool _suppressNavigate;

    public ObservableCollection<OutlineNode> Roots { get; } = new();

    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;

    private OutlineNode? _selectedNode;
    /// <summary>Two-way bound to the tree; selecting a node navigates the editor to its line.</summary>
    public OutlineNode? SelectedNode
    {
        get => _selectedNode;
        set { if (SetProperty(ref _selectedNode, value) && !_suppressNavigate) NavigateTo(value); }
    }

    public OutlineViewModel() { } // design-time

    public OutlineViewModel(IDocumentService documents)
    {
        _documents = documents;
        _documents.ActiveDocumentChanged += (_, _) => Rebuild();
        _documents.DocumentChanged += (_, _) => Rebuild();
        Rebuild();
    }

    partial void OnFilterChanged(string value) => Rebuild();

    private void Rebuild()
    {
        _suppressNavigate = true;
        try
        {
            Roots.Clear();
            var doc = _documents?.Active;
            _currentFile = doc?.FilePath;
            if (doc is null || string.IsNullOrEmpty(doc.DocumentText)) { IsEmpty = true; return; }

            var tree = BuildTree(doc.DocumentText);
            if (!string.IsNullOrWhiteSpace(Filter)) tree = Prune(tree, Filter.Trim());
            foreach (var n in tree) Roots.Add(n);
            IsEmpty = Roots.Count == 0;
        }
        finally { _suppressNavigate = false; }
    }

    private static List<OutlineNode> BuildTree(string text)
    {
        var roots = new List<OutlineNode>();
        var stack = new Stack<OutlineNode>();
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
            }
            else if (TherionBlocks.CloserType(fw) is { } ctype)
            {
                while (stack.Count > 0 && stack.Peek().Kind != ctype) stack.Pop();
                if (stack.Count > 0) stack.Pop();
            }
        }
        return roots;
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

    private void NavigateTo(OutlineNode? node)
    {
        if (node is null || _documents is null || string.IsNullOrEmpty(_currentFile)) return;
        var loc = new SourceLocation(node.Line, 1);
        _ = _documents.NavigateToSpanAsync(new SourceSpan(_currentFile!, loc, loc, 0, 0));
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
