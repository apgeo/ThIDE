// Implementation Plan §7.3 — Diagnostics panel ViewModel.
// Aggregates parser + semantic + compile diagnostics into a single list for the Diagnostics tool
// window. DIAG-07 adds search, severity/category filtering, per-code suppression and F8
// next/prev navigation; DIAG-09 surfaces a plain-language explanation + thbook link per code.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Processing.Abstractions;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public sealed record DiagnosticRow(
    string Code,
    string Severity,
    string Message,
    string File,
    int Line,
    int Column,
    SourceSpan Span)
{
    /// <summary>Just the file name (the dedicated File Name column, #3).</summary>
    public string FileName => string.IsNullOrEmpty(File) ? string.Empty : System.IO.Path.GetFileName(File);

    /// <summary>Tab-separated row text for clipboard copy (#3).</summary>
    public string ToClipboardText() => $"{Severity}\t{Code}\t{FileName}\t{Line}\t{Message}";
}

public partial class DiagnosticsViewModel : ViewModelBase
{
    // Reference-resolution codes (DIAG-06): the "References" category filter narrows to these.
    private static readonly HashSet<string> ReferenceCodes = new(StringComparer.Ordinal)
    {
        "TH_SEM_001", "TH_SEM_003", "TH_SEM_014", "TH_WS_001",
    };

    private readonly IThbookDocumentationService? _docs;
    private IReadOnlyList<DiagnosticRow> _allRows = Array.Empty<DiagnosticRow>();
    private readonly HashSet<string> _suppressed = new(StringComparer.Ordinal);

    [ObservableProperty] private IReadOnlyList<DiagnosticRow> _rows = Array.Empty<DiagnosticRow>();
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private DiagnosticRow? _selected;
    [ObservableProperty] private bool _showProjectScope;

    // ---- DIAG-07 filters ----
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showErrors = true;
    [ObservableProperty] private bool _showWarnings = true;
    [ObservableProperty] private bool _showInfos = true;
    [ObservableProperty] private bool _onlyReferences;
    [ObservableProperty] private int _suppressedCount;

    public bool HasSuppressions => SuppressedCount > 0;
    partial void OnSuppressedCountChanged(int value) => OnPropertyChanged(nameof(HasSuppressions));

    /// <summary>True when there is at least one error or warning (drives the status-bar badge, DIAG-07).</summary>
    public bool HasProblems => ErrorCount > 0 || WarningCount > 0;
    partial void OnErrorCountChanged(int value) => OnPropertyChanged(nameof(HasProblems));
    partial void OnWarningCountChanged(int value) => OnPropertyChanged(nameof(HasProblems));

    // ---- DIAG-09 explanation of the selected diagnostic ----
    [ObservableProperty] private string _selectedExplanation = string.Empty;
    [ObservableProperty] private string _selectedExample = string.Empty;
    [ObservableProperty] private bool _hasExplanation;
    [ObservableProperty] private bool _hasExample;
    [ObservableProperty] private bool _hasDocLink;
    private string? _selectedDocTerm;

    /// <summary>Number of distinct files that have at least one warning or error (#3).</summary>
    [ObservableProperty] private int _filesWithIssues;

    public DiagnosticsViewModel() { } // design-time
    public DiagnosticsViewModel(IThbookDocumentationService? docs) => _docs = docs;

    /// <summary>Human-readable roll-up shown beside the scope toggle (#3).</summary>
    public string SummaryText
    {
        get
        {
            if (ErrorCount == 0 && WarningCount == 0) return "No warnings or errors";
            string warns = $"{WarningCount} warning{(WarningCount == 1 ? "" : "s")}";
            string errs = $"{ErrorCount} error{(ErrorCount == 1 ? "" : "s")}";
            string files = $"{FilesWithIssues} file{(FilesWithIssues == 1 ? "" : "s")}";
            string suff = SuppressedCount > 0 ? $"  ·  {SuppressedCount} code(s) suppressed" : "";
            return $"{warns} and {errs} in {files}{suff}";
        }
    }

    public event EventHandler<DiagnosticRow>? NavigateRequested;
    /// <summary>Raised when <see cref="ShowProjectScope"/> changes, so the shell can reload with the correct scope.</summary>
    public event EventHandler? ScopeChanged;

    partial void OnShowProjectScopeChanged(bool value) => ScopeChanged?.Invoke(this, EventArgs.Empty);
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowErrorsChanged(bool value) => ApplyFilter();
    partial void OnShowWarningsChanged(bool value) => ApplyFilter();
    partial void OnShowInfosChanged(bool value) => ApplyFilter();
    partial void OnOnlyReferencesChanged(bool value) => ApplyFilter();
    partial void OnSelectedChanged(DiagnosticRow? value) => UpdateExplanation(value);

    public void Load(IEnumerable<Diagnostic> diagnostics)
    {
        _allRows = diagnostics
            .Select(d => new DiagnosticRow(
                d.Code, d.Severity.ToString(), d.Message,
                d.Span.FilePath ?? string.Empty, d.Span.Start.Line, d.Span.Start.Column, d.Span))
            .OrderBy(r => r.File, StringComparer.Ordinal)
            .ThenBy(r => r.Line)
            .ToList();

        // Totals reflect ALL diagnostics (badge counts), independent of the visible filter.
        ErrorCount = _allRows.Count(r => r.Severity == nameof(DiagnosticSeverity.Error));
        WarningCount = _allRows.Count(r => r.Severity == nameof(DiagnosticSeverity.Warning));
        FilesWithIssues = _allRows
            .Where(r => r.Severity is nameof(DiagnosticSeverity.Error) or nameof(DiagnosticSeverity.Warning))
            .Select(r => r.File).Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        IEnumerable<DiagnosticRow> rows = _allRows;

        if (_suppressed.Count > 0) rows = rows.Where(r => !_suppressed.Contains(r.Code));
        if (OnlyReferences) rows = rows.Where(r => ReferenceCodes.Contains(r.Code));
        rows = rows.Where(r => SeverityVisible(r.Severity));
        if (q.Length > 0)
            rows = rows.Where(r =>
                r.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Code.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.FileName.Contains(q, StringComparison.OrdinalIgnoreCase));

        Rows = rows.ToList();
        SuppressedCount = _suppressed.Count;
        OnPropertyChanged(nameof(SummaryText));
    }

    private bool SeverityVisible(string severity) => severity switch
    {
        nameof(DiagnosticSeverity.Error) => ShowErrors,
        nameof(DiagnosticSeverity.Warning) => ShowWarnings,
        _ => ShowInfos,
    };

    private void UpdateExplanation(DiagnosticRow? row)
    {
        var ex = row is null ? null : DiagnosticExplanations.For(row.Code);
        SelectedExplanation = ex?.Summary ?? string.Empty;
        SelectedExample = ex?.Example ?? string.Empty;
        HasExplanation = ex is not null;
        HasExample = !string.IsNullOrEmpty(ex?.Example);
        _selectedDocTerm = ex?.DocTerm;
        HasDocLink = _docs is { IsAvailable: true } && !string.IsNullOrEmpty(_selectedDocTerm)
                     && _docs.TryGetPage(_selectedDocTerm!, out _);
    }

    /// <summary>All rows as text for "Copy All" (#3).</summary>
    public string AllRowsAsText() => string.Join(Environment.NewLine, Rows.Select(r => r.ToClipboardText()));

    public void Clear() => Load(ImmutableArray<Diagnostic>.Empty);

    [RelayCommand]
    private void Navigate(DiagnosticRow? row)
    {
        if (row is null) return;
        NavigateRequested?.Invoke(this, row);
    }

    /// <summary>DIAG-07: hide every diagnostic sharing the selected row's code for this session.</summary>
    [RelayCommand]
    private void SuppressSelectedCode()
    {
        if (Selected?.Code is { Length: > 0 } code && _suppressed.Add(code)) ApplyFilter();
    }

    [RelayCommand]
    private void ClearSuppressions()
    {
        if (_suppressed.Count == 0) return;
        _suppressed.Clear();
        ApplyFilter();
    }

    /// <summary>DIAG-09: open the thbook page for the selected diagnostic's topic.</summary>
    [RelayCommand]
    private void OpenDocs()
    {
        if (!string.IsNullOrEmpty(_selectedDocTerm)) _docs?.Open(_selectedDocTerm!);
    }

    /// <summary>F8 / Shift+F8: select and navigate to the next / previous visible diagnostic.</summary>
    public void GoToAdjacent(bool forward)
    {
        if (Rows.Count == 0) return;
        int idx = Selected is null ? -1 : IndexOf(Rows, Selected);
        int next = forward ? (idx + 1) % Rows.Count : (idx - 1 + Rows.Count) % Rows.Count;
        Selected = Rows[next];
        NavigateRequested?.Invoke(this, Rows[next]);
    }

    private static int IndexOf(IReadOnlyList<DiagnosticRow> rows, DiagnosticRow row)
    {
        for (int i = 0; i < rows.Count; i++) if (ReferenceEquals(rows[i], row)) return i;
        return -1;
    }
}
