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
using TherionProc.Services;
using TherionProc.ViewModels.Docking;

namespace TherionProc.ViewModels;

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

    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (string.IsNullOrEmpty(Query)) { Status = "Enter text to find."; return; }
        if (!TryBuildRegex(out var regex)) return;

        Status = "Replacing…";
        var (paths, openText) = ResolveTargets();
        var replacement = Replacement;
        bool useRegexReplacement = UseRegex; // honour $1/$& substitutions only in regex mode

        // Compute the new text per file off the UI thread; collect what actually changed.
        var changes = await Task.Run(() =>
        {
            var list = new List<(string Path, string NewText, int Count)>();
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
                    list.Add((path, newText, count));
            }
            return list;
        }).ConfigureAwait(true);

        int fileCount = 0, total = 0;
        foreach (var (path, newText, count) in changes)
        {
            // Live editor buffer takes precedence: update the open document (reflected + reparsed)
            // and persist it; otherwise write the file on disk directly.
            var openDoc = _documents.Documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
            try
            {
                File.WriteAllText(path, newText);
                openDoc?.SetText(newText, reparse: true);
                fileCount++;
                total += count;
            }
            catch { /* skip files that fail to write (locked / read-only) */ }
        }

        Results = Array.Empty<SearchHit>();
        Status = $"Replaced {total} occurrence(s) in {fileCount} file(s).";
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
