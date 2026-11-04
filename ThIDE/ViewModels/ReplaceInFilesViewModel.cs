// Global "Replace in Files" (#9), Visual-Studio-style: find + replace across the project
// (files related to the active .thconfig), a directory, or the open documents; with
// match-case, whole-word and regex options and a file-type mask. "Find All" previews the
// matches; "Replace All" rewrites every matching file (and live-updates any open editor), after a
// confirmation when it reaches more than one file, and records it for the workspace undo.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.ViewModels;

public sealed partial class ReplaceInFilesViewModel : ViewModelBase
{
    private const int MaxResults = 5000;
    private const string DefaultMask = "*.th;*.th2;*.thconfig;*.thc";
    private readonly IDocumentService _documents;
    private readonly IWorkspaceUndoService _history;
    private readonly IWorkspaceSession? _session;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceAllCommand))]
    private string _query = string.Empty;
    [ObservableProperty] private string _replacement = string.Empty;
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _useRegex;
    [ObservableProperty] private string _directory = string.Empty;
    [ObservableProperty] private string _fileMask = DefaultMask;
    [ObservableProperty] private string _scope = ScopeProject;
    [ObservableProperty] private IReadOnlyList<SearchHit> _results = Array.Empty<SearchHit>();
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private SearchHit? _selected;

    /// <summary>True when the Directory scope is selected (shows the directory/mask row).</summary>
    public bool IsDirectoryScope => Scope == ScopeDirectory;
    partial void OnScopeChanged(string value) => OnPropertyChanged(nameof(IsDirectoryScope));

    // Option ids; the combo shows them as Opt_Scope_<id>.
    public const string ScopeProject = "Project";
    public const string ScopeDirectory = "Directory";
    public const string ScopeOpenFiles = "OpenFiles";
    public string[] Scopes { get; } = { ScopeProject, ScopeDirectory, ScopeOpenFiles };

    public ReplaceInFilesViewModel() : this(new NullDocumentService()) { }

    /// <param name="history">The workspace undo history Replace All records into; a private one when null.</param>
    /// <param name="session">The workspace session whose file watcher is told about this panel's own writes, so they are not reported as external changes.</param>
    public ReplaceInFilesViewModel(IDocumentService documents, IWorkspaceUndoService? history = null, IWorkspaceSession? session = null)
    {
        _documents = documents;
        _session = session;
        _history = history ?? new WorkspaceUndoService(documents, session);
        _history.Changed += (_, _) => OnHistoryChanged();
    }

    // A Replace All undone or redone elsewhere (Edit ▸ Undo, the notification's Undo) changed the files
    // the list shows: search again so it does not list lines that are gone.
    private void OnHistoryChanged()
    {
        NotifyHistory();
        if (_running || string.IsNullOrEmpty(Query)) return;
        if (_history.NextUndo is { } u && _mine.Contains(u) || _history.NextRedo is { } r && _mine.Contains(r))
            _ = FindAll();
    }

    // Set while this panel itself replaces, undoes or redoes (it searches again on its own).
    private bool _running;

    /// <summary>
    /// Asks the user to go ahead (the question in the UI language); true to replace. Set by the view.
    /// Replace All asks only when it would change more than one file; with no confirmation set it does not ask.
    /// </summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

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

    /// <summary>One file's before/after content in a replace batch.</summary>
    private sealed record ReplaceEdit(string Path, string OldText, string NewText, int Count);

    // Replace All operations recorded by this panel. They live in the workspace undo history (Edit ▸
    // Undo reverts them too); the panel's own Undo/Redo act only while the next step is one of these.
    private readonly HashSet<WorkspaceUndoRecord> _mine = [];

    public bool CanUndo => _history.NextUndo is { } r && _mine.Contains(r);
    public bool CanRedo => _history.NextRedo is { } r && _mine.Contains(r);

    private bool CanReplaceAll() => !string.IsNullOrEmpty(Query);

    [RelayCommand(CanExecute = nameof(CanReplaceAll))]
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
            foreach (var path in paths.Distinct(FilePaths.Comparer))
            {
                string? text = openText.TryGetValue(path, out var t) ? t : ReadFile(path);
                if (text is null) continue;
                var newText = ReplaceLines(text, regex, replacement, useRegexReplacement, out int count);
                if (count == 0) continue;
                if (!string.Equals(newText, text, StringComparison.Ordinal))
                    list.Add(new ReplaceEdit(path, text, newText, count));
            }
            return list;
        }).ConfigureAwait(true);

        int total = changes.Sum(c => c.Count);
        if (changes.Count > 1 && Confirm is { } confirm
            && !await confirm(string.Format(ThIDE.Resources.Tr.Get("Replace_Confirm"), ThIDE.Resources.Tr.Plural("Rename_Occurrences", total), ThIDE.Resources.Tr.Plural("Rename_Files", changes.Count))).ConfigureAwait(true))
        {
            Status = string.Empty;
            return;
        }

        int fileCount = ApplyEdits(changes.Select(c => (c.Path, c.NewText)));
        if (fileCount < 0) return;

        var summary = string.Format(ThIDE.Resources.Tr.Get("Replace_Done"), ThIDE.Resources.Tr.Plural("Rename_Occurrences", total), ThIDE.Resources.Tr.Plural("Rename_Files", fileCount));
        if (changes.Count > 0)
        {
            var record = new WorkspaceUndoRecord(ThIDE.Resources.Tr.Get("Replace_Title"), summary,
                changes.Select(c => new FileChange(c.Path, c.OldText, c.NewText)).ToList());
            _mine.Add(record);
            _running = true;
            try { _history.Push(record); }   // a new operation invalidates the redo history
            finally { _running = false; }
        }

        // The query stays; searching it again lists what is left of it (normally nothing).
        await FindAll().ConfigureAwait(true);
        Status = summary;
    }

    // Replaces line by line, exactly the units ScanText previews (a line's '\r' is not part of it), so
    // Replace All changes the matches Find All listed: '^' anchors a line and no match spans lines.
    private static string ReplaceLines(string text, Regex regex, string replacement, bool expand, out int count)
    {
        count = 0;
        var sb = new System.Text.StringBuilder(text.Length);
        int lineStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n') continue;

            int end = i > lineStart && text[i - 1] == '\r' ? i - 1 : i;
            var line = text[lineStart..end];
            int n = regex.Matches(line).Count;
            if (n == 0) sb.Append(line);
            else
            {
                count += n;
                sb.Append(expand ? regex.Replace(line, replacement)
                                 : regex.Replace(line, _ => replacement)); // literal (no $-expansion)
            }
            sb.Append(text, end, Math.Min(i + 1, text.Length) - end); // the line terminator, if any
            lineStart = i + 1;
        }
        return sb.ToString();
    }

    /// <summary>Reverts the last Replace-All (restores every file's previous content), keeping it redoable.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task Undo() => RunHistoryAsync(undo: true);

    /// <summary>Re-applies the last undone Replace-All across every file it touched.</summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    private Task Redo() => RunHistoryAsync(undo: false);

    // The workspace history only runs a step while every file still holds the text the batch left
    // (open buffer, else disk); otherwise a later edit would be overwritten, and it refuses the step.
    // The search runs again afterwards, so the list shows the files as they are now.
    private async Task RunHistoryAsync(bool undo)
    {
        if (!(undo ? CanUndo : CanRedo)) return;
        WorkspaceUndoOutcome outcome;
        _running = true;
        try { outcome = undo ? await _history.UndoAsync().ConfigureAwait(true) : await _history.RedoAsync().ConfigureAwait(true); }
        finally { _running = false; }
        if (outcome.Status == WorkspaceUndoStatus.Done) await FindAll().ConfigureAwait(true);
        if (outcome.Message is { } message) Status = message;
    }

    // Writes each (path, text): the live editor buffer takes precedence (reflected + reparsed) and is
    // persisted; otherwise the file is written on disk directly. Every file is encoded before any is
    // written, so one whose encoding cannot hold the new text refuses the whole batch (returns -1 and
    // Status says why) instead of leaving it half-applied. Returns the number of files written.
    private int ApplyEdits(IEnumerable<(string Path, string Text)> edits)
    {
        var planned = new List<(string Path, string Text, byte[] Bytes, TextFileFormat Format, FileDocumentViewModel? Doc)>();
        foreach (var (path, text) in edits)
        {
            var openDoc = _documents.Documents.FirstOrDefault(d =>
                FilePaths.Comparer.Equals(d.FilePath, path));
            try
            {
                var (bytes, format) = SourceFileIo.EncodeForSave(path, text, openDoc?.Format);
                planned.Add((path, text, bytes, format, openDoc));
            }
            catch (UnrepresentableCharacterException ex)
            {
                Status = string.Format(ThIDE.Resources.Tr.Get(ex is UndecodableBytesException ? "Replace_UndecodableRefused" : "Replace_EncodingRefused"),
                    System.IO.Path.GetFileName(path), ex.Character, ex.EncodingName);
                return -1;
            }
            catch { /* unreadable: skipped, as a failed write is below */ }
        }

        int fileCount = 0;
        foreach (var (path, text, bytes, format, openDoc) in planned)
        {
            _session?.SuppressSelfWrite(path);
            try
            {
                SourceFileIo.WriteOver(path, bytes);
                if (openDoc is not null) openDoc.Format = format;
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
            .GroupBy(d => d.FilePath, FilePaths.Comparer)
            .ToDictionary(g => g.Key, g => g.First().DocumentText, FilePaths.Comparer);

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
        foreach (var path in paths.Distinct(FilePaths.Comparer))
        {
            if (hits.Count >= MaxResults) break;
            string? text = openText.TryGetValue(path, out var t) ? t : ReadFile(path);
            if (text is null) continue;
            int before = hits.Count;
            ScanText(path, text, regex, hits);
            if (hits.Count > before) files++;   // "in N files" counts the files with a match, not those searched
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
        var set = new HashSet<string>(FilePaths.Comparer);
        if (_documents.Workspace is { } ws)
        {
            foreach (var p in ws.PerFile.Keys) set.Add(p);
            foreach (var (from, to, _) in ws.FileGraphEdges) { set.Add(from); set.Add(to); }
        }
        foreach (var d in _documents.Documents)
            if (!string.IsNullOrEmpty(d.FilePath)) set.Add(d.FilePath);
        return set;
    }

    private static IEnumerable<string> EnumerateDirectory(string dir, string mask)
    {
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) yield break;
        var patterns = mask.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0) patterns = DefaultMask.Split(';');
        // Not into symlinked folders: a link in a shared project would make Replace All rewrite files
        // outside the directory the user chose.
        foreach (var pattern in patterns)
            foreach (var file in ProjectFileWalk.EnumerateFiles(dir, pattern))
                yield return file;
    }

    private static string? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            if (Array.IndexOf(bytes, (byte)0) >= 0) return null;   // binary
            // Read as the save reads it: a file holding bytes its encoding cannot decode is skipped, since
            // rewriting it would replace them (an ASCII file shows them as '?', not U+FFFD, in a plain decode).
            var file = SourceFileIo.Read(path, bytes);
            return file.Lossy ? null : file.Text;
        }
        catch { return null; }
    }
}
