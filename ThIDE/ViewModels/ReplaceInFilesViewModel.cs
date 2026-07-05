// Global "Replace in Files" (#9), Visual-Studio-style: find + replace across the project
// (files related to the active .thconfig), a directory, or the open documents; with
// match-case, whole-word and regex options and a file-type mask. "Find All" previews the
// matches; "Replace All" rewrites every matching file (and live-updates any open editor).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.ViewModels;

public sealed partial class ReplaceInFilesViewModel : ViewModelBase
{
    private const int MaxResults = 5000;
    private readonly IDocumentService _documents;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private string _replacement = string.Empty;
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private string _directory = string.Empty;
    [ObservableProperty] private string _fileMask = "*.th;*.th2;*.thconfig;*.thc";
    [ObservableProperty] private string _scope = ScopeProject;
    [ObservableProperty] private IReadOnlyList<SearchHit> _results = Array.Empty<SearchHit>();
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private SearchHit? _selected;

    /// <summary>True when the Directory scope is selected (shows the directory/mask row).</summary>
    public bool IsDirectoryScope => Scope == ScopeDirectory;
    partial void OnScopeChanged(string value) => OnPropertyChanged(nameof(IsDirectoryScope));

    public const string ScopeProject = "Project (active thconfig)";
    public const string ScopeDirectory = "Directory";
    public const string ScopeOpenFiles = "Open files";
    public string[] Scopes { get; } = { ScopeProject, ScopeDirectory, ScopeOpenFiles };

    public ReplaceInFilesViewModel() : this(new NullDocumentService()) { }

    public ReplaceInFilesViewModel(IDocumentService documents) => _documents = documents;

    /// <summary>
    /// Prepares the panel each time it is opened: clears any stale status/results and seeds the search
    /// directory. When the editor had a selection, it is put in the Find box and Find All runs at once
    /// (like pressing the button); with no selection the previous query is kept (and not re-run).
    /// </summary>
    public async Task PrepareForOpenAsync(string? selection)
    {
        PrepareDefaults();
        Status = string.Empty;
        if (!string.IsNullOrEmpty(selection))
        {
            Query = selection;
            await FindAll().ConfigureAwait(true);
        }
        else
        {
            Results = Array.Empty<SearchHit>();
        }
    }

    /// <summary>Seeds the default search directory (the active project's root folder).</summary>
    public void PrepareDefaults()
    {
        if (!string.IsNullOrEmpty(Directory)) return;
        if (_documents.CurrentPath is { } cur && !string.IsNullOrEmpty(cur))
        {
            try
            {
                var entry = ProjectEntryDiscovery.FindEntryPoint(cur);
                Directory = Path.GetDirectoryName(entry) ?? Path.GetDirectoryName(cur) ?? string.Empty;
            }
            catch { Directory = Path.GetDirectoryName(cur) ?? string.Empty; }
        }
    }

    private bool TryBuildRegex(out Regex regex)
    {
        regex = null!;
        try
        {
            var pattern = UseRegex ? Query : Regex.Escape(Query);
            if (WholeWord) pattern = $@"\b(?:{pattern})\b";
            regex = new Regex(pattern, MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            return true;
        }
        catch (Exception ex) { Status = "Invalid pattern: " + ex.Message; return false; }
    }

    [RelayCommand]
    private async Task FindAll()
    {
        if (string.IsNullOrEmpty(Query)) { Results = Array.Empty<SearchHit>(); Status = string.Empty; return; }
        if (!TryBuildRegex(out var regex)) return;

        Status = "Searching…";
        var (paths, openText) = ResolveTargets();
        var (hits, fileCount) = await Task.Run(() => Scan(regex, paths, openText)).ConfigureAwait(true);

        Results = hits;
        Status = hits.Count >= MaxResults
            ? $"{hits.Count}+ matches (truncated) in {fileCount} files"
            : $"{hits.Count} matches in {fileCount} files";
    }

    /// <summary>One file's before/after content in a replace batch (for global undo/redo).</summary>
    private sealed record ReplaceEdit(string Path, string OldText, string NewText, int Count);

    // Global (cross-file) undo/redo stacks for whole Replace-All operations. Each entry reverts/re-applies
    // every file the operation touched, so a project-wide replace is a single reversible step.
    private readonly Stack<IReadOnlyList<ReplaceEdit>> _undo = new();
    private readonly Stack<IReadOnlyList<ReplaceEdit>> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (string.IsNullOrEmpty(Query)) { Status = "Enter text to find."; return; }
        if (!TryBuildRegex(out var regex)) return;

        Status = "Replacing…";
        var (paths, openText) = ResolveTargets();
        var replacement = Replacement;
        bool useRegexReplacement = UseRegex; // honour $1/$& substitutions only in regex mode

        // Compute the new text per file off the UI thread; collect what actually changed (with the
        // old text so the whole batch can be undone/redone).
        var changes = await Task.Run(() =>
        {
            var list = new List<ReplaceEdit>();
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string? text = openText.TryGetValue(path, out var t) ? t : ReadFile(path);
                if (text is null) continue;
                int count = regex.Matches(text).Count;
                if (count == 0) continue;
                var newText = useRegexReplacement
                    ? regex.Replace(text, replacement)
                    : regex.Replace(text, _ => replacement); // literal replacement (no $-expansion)
                if (!string.Equals(newText, text, StringComparison.Ordinal))
                    list.Add(new ReplaceEdit(path, text, newText, count));
            }
            return list;
        }).ConfigureAwait(true);

        int fileCount = ApplyEdits(changes.Select(c => (c.Path, c.NewText)));
        int total = changes.Sum(c => c.Count);

        if (changes.Count > 0)
        {
            _undo.Push(changes);   // a new operation invalidates the redo history
            _redo.Clear();
            NotifyHistory();
        }

        // Re-search for the replacement so the list shows where it landed (like Find All on the new value).
        var summary = $"Replaced {total} occurrence(s) in {fileCount} file(s).";
        Query = Replacement;
        await FindAll().ConfigureAwait(true);
        Status = summary;
    }

    /// <summary>Reverts the last Replace-All (restores every file's previous content), keeping it redoable.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.Count == 0) return;
        var batch = _undo.Pop();
        int n = ApplyEdits(batch.Select(e => (e.Path, e.OldText)));
        _redo.Push(batch);
        NotifyHistory();
        Status = $"Undid replace in {n} file(s).";
    }

    /// <summary>Re-applies the last undone Replace-All across every file it touched.</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redo.Count == 0) return;
        var batch = _redo.Pop();
        int n = ApplyEdits(batch.Select(e => (e.Path, e.NewText)));
        _undo.Push(batch);
        NotifyHistory();
        Status = $"Redid replace in {n} file(s).";
    }

    // Writes each (path, text): the live editor buffer takes precedence (reflected + reparsed) and is
    // persisted; otherwise the file is written on disk directly. Returns the number of files written.
    private int ApplyEdits(IEnumerable<(string Path, string Text)> edits)
    {
        int fileCount = 0;
        foreach (var (path, text) in edits)
        {
            var openDoc = _documents.Documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
            try
            {
                File.WriteAllText(path, text);
                openDoc?.SetText(text, reparse: true);
                fileCount++;
            }
            catch { /* skip files that fail to write (locked / read-only) */ }
        }
        return fileCount;
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Activate(SearchHit? hit)
    {
        if (hit is { } h) _ = _documents.NavigateToSpanAsync(h.Span);
    }

    // ---- target resolution + scan ------------------------------------------

    private (IEnumerable<string> Paths, Dictionary<string, string> OpenText) ResolveTargets()
    {
        var openText = _documents.Documents
            .Where(d => !string.IsNullOrEmpty(d.FilePath))
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DocumentText, StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> paths = Scope switch
        {
            ScopeOpenFiles => openText.Keys.ToList(),
            ScopeDirectory => EnumerateDirectory(Directory, FileMask),
            _              => ProjectFiles(),
        };
        return (paths, openText);
    }

    private (List<SearchHit> Hits, int Files) Scan(
        Regex regex, IEnumerable<string> paths, Dictionary<string, string> openText)
    {
        var hits = new List<SearchHit>();
        int files = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (hits.Count >= MaxResults) break;
            string? text = openText.TryGetValue(path, out var t) ? t : ReadFile(path);
            if (text is null) continue;
            files++;
            ScanText(path, text, regex, hits);
        }
        return (hits, files);
    }

    private static void ScanText(string path, string text, Regex regex, List<SearchHit> hits)
    {
        var name = Path.GetFileName(path);
        int line = 1, lineStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n') continue;

            int end = i > lineStart && text[i - 1] == '\r' ? i - 1 : i;
            var lineText = text[lineStart..end];
            foreach (Match m in regex.Matches(lineText))
            {
                int col = m.Index + 1;
                var span = new SourceSpan(path,
                    new SourceLocation(line, col), new SourceLocation(line, col + m.Length),
                    lineStart + m.Index, m.Length);
                hits.Add(new SearchHit(path, $"{name}({line}): {lineText.Trim()}", span));
                if (hits.Count >= MaxResults) return;
            }
            line++;
            lineStart = i + 1;
        }
    }

    private IReadOnlyCollection<string> ProjectFiles()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_documents.Workspace is { } ws)
        {
            foreach (var p in ws.PerFile.Keys) set.Add(p);
            foreach (var (from, to) in ws.FileGraphEdges) { set.Add(from); set.Add(to); }
        }
        foreach (var d in _documents.Documents)
            if (!string.IsNullOrEmpty(d.FilePath)) set.Add(d.FilePath);
        return set;
    }

    private static IEnumerable<string> EnumerateDirectory(string dir, string mask)
    {
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) yield break;
        var patterns = mask.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0) patterns = new[] { "*" };
        foreach (var pattern in patterns)
        {
            IEnumerable<string> found;
            try { found = System.IO.Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var f in found) yield return f;
        }
    }

    private static string? ReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }
}
