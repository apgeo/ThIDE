// Global "Find in Files" (#3), VS-style: search the project (files related to the
// active .thconfig), a directory, or the open documents; with match-case, whole-word
// and regex options. Results navigate to the match via the document service.

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

namespace TherionProc.ViewModels;

public sealed record SearchHit(string FilePath, string Display, SourceSpan Span);

public sealed partial class SearchViewModel : ViewModelBase
{
    private const int MaxResults = 5000;
    private readonly IDocumentService _documents;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private string _directory = string.Empty;
    [ObservableProperty] private string _fileMask = "*.th;*.th2;*.thconfig;*.thc";
    [ObservableProperty] private string _scope = ScopeProject;
    [ObservableProperty] private IReadOnlyList<SearchHit> _results = Array.Empty<SearchHit>();

    /// <summary>True when the Directory scope is selected (shows the directory/mask row).</summary>
    public bool IsDirectoryScope => Scope == ScopeDirectory;
    partial void OnScopeChanged(string value) => OnPropertyChanged(nameof(IsDirectoryScope));
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private SearchHit? _selected;

    public const string ScopeProject = "Project";
    public const string ScopeDirectory = "Directory";
    public const string ScopeOpenFiles = "Open files";
    public string[] Scopes { get; } = { ScopeProject, ScopeDirectory, ScopeOpenFiles };

    public SearchViewModel() : this(new NullDocumentService()) { }

    public SearchViewModel(IDocumentService documents) => _documents = documents;

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

    [RelayCommand]
    private async Task Search()
    {
        var query = Query;
        if (string.IsNullOrEmpty(query)) { Results = Array.Empty<SearchHit>(); Status = string.Empty; return; }

        Regex regex;
        try
        {
            var pattern = UseRegex ? query : Regex.Escape(query);
            if (WholeWord) pattern = $@"\b(?:{pattern})\b";
            regex = new Regex(pattern, MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        }
        catch (Exception ex) { Status = "Invalid pattern: " + ex.Message; return; }

        Status = "Searching…";
        var openText = _documents.Documents
            .Where(d => !string.IsNullOrEmpty(d.FilePath))
            .GroupBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DocumentText, StringComparer.OrdinalIgnoreCase);

        var scope = Scope;
        var dir = Directory;
        var mask = FileMask;
        var projectFiles = scope == ScopeProject ? ProjectFiles() : null;

        var (hits, fileCount) = await Task.Run(() =>
            Run(regex, scope, dir, mask, openText, projectFiles)).ConfigureAwait(true);

        Results = hits;
        Status = hits.Count >= MaxResults
            ? $"{hits.Count}+ matches (truncated) in {fileCount} files"
            : $"{hits.Count} matches in {fileCount} files";
    }

    [RelayCommand]
    private void Activate(SearchHit? hit)
    {
        if (hit is { } h) _ = _documents.NavigateToSpanAsync(h.Span);
    }

    // ---- search execution ----------------------------------------------

    private (List<SearchHit> Hits, int Files) Run(
        Regex regex, string scope, string dir, string mask,
        Dictionary<string, string> openText, IReadOnlyCollection<string>? projectFiles)
    {
        var hits = new List<SearchHit>();
        int files = 0;

        IEnumerable<string> paths = scope switch
        {
            ScopeOpenFiles => openText.Keys,
            ScopeDirectory => EnumerateDirectory(dir, mask),
            _              => projectFiles ?? Array.Empty<string>(),
        };

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
