// Implementation Plan �7.3 � Diagnostics panel ViewModel.
// Aggregates parser + semantic + compile diagnostics into a single flat list
// for the Diagnostics tool window (click-to-navigate via Navigation property).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace TherionProc.ViewModels;

public sealed record DiagnosticRow(
    string Code,
    string Severity,
    string Message,
    string File,
    int Line,
    int Column,
    SourceSpan Span);

public partial class DiagnosticsViewModel : ViewModelBase
{
    [ObservableProperty] private IReadOnlyList<DiagnosticRow> _rows = Array.Empty<DiagnosticRow>();
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private DiagnosticRow? _selected;
    [ObservableProperty] private bool _showProjectScope;

    public event EventHandler<DiagnosticRow>? NavigateRequested;
    /// <summary>Raised when <see cref="ShowProjectScope"/> changes, so the shell can reload with the correct scope.</summary>
    public event EventHandler? ScopeChanged;

    partial void OnShowProjectScopeChanged(bool value) => ScopeChanged?.Invoke(this, EventArgs.Empty);

    public void Load(IEnumerable<Diagnostic> diagnostics)
    {
        var rows = diagnostics
            .Where(d => d.Span.Start.Line > 0 || !string.IsNullOrEmpty(d.Span.FilePath) || true)
            .Select(d => new DiagnosticRow(
                d.Code,
                d.Severity.ToString(),
                d.Message,
                d.Span.FilePath ?? string.Empty,
                d.Span.Start.Line,
                d.Span.Start.Column,
                d.Span))
            .OrderBy(r => r.File, StringComparer.Ordinal)
            .ThenBy(r => r.Line)
            .ToList();

        Rows = rows;
        ErrorCount   = rows.Count(r => r.Severity == nameof(DiagnosticSeverity.Error));
        WarningCount = rows.Count(r => r.Severity == nameof(DiagnosticSeverity.Warning));
    }

    public void Clear() => Load(ImmutableArray<Diagnostic>.Empty);

    [RelayCommand]
    private void Navigate(DiagnosticRow? row)
    {
        if (row is null) return;
        NavigateRequested?.Invoke(this, row);
    }
}
