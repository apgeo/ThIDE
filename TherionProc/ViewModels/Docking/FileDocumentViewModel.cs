// One open .th/.th2/.thconfig file as a Dock document (multi-file MDI).
// Carries its own parsed model + Measurements grid, so each document tab is a
// fully independent view of its file and can be floated/docked on its own.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Services;

namespace TherionProc.ViewModels.Docking;

public sealed class FileDocumentViewModel : Document, IDockContent
{
    private readonly ICommandRegistry? _commands;

    private string _documentText = string.Empty;
    private TherionFile? _ast;
    private SemanticModel? _semantics;
    private ISymbolNavigationService? _navigation;
    private ImmutableArray<Diagnostic> _diagnostics = ImmutableArray<Diagnostic>.Empty;

    /// <summary>Raised when something wants the editor to scroll to a span (e.g. diagnostics).</summary>
    public event EventHandler<SourceSpan>? ScrollToSpanRequested;

    /// <summary>Raised after a re-parse so document-tracking tools can refresh.</summary>
    public event EventHandler? Reparsed;

    public string FilePath { get; }

    public MeasurementsViewModel Measurements { get; }

    public FileDocumentViewModel(string filePath, string text, MeasurementsViewModel measurements,
        ICommandRegistry? commands = null)
    {
        FilePath = filePath;
        _commands = commands;
        Measurements = measurements;

        Id = filePath;
        Title = System.IO.Path.GetFileName(filePath);
        CanFloat = true;
        CanClose = true;
        CanPin = true;

        SetText(text, reparse: true);
    }

    public string DocumentText
    {
        get => _documentText;
        set { if (SetProperty(ref _documentText, value)) Reparse(); }
    }

    public TherionFile? Ast { get => _ast; private set => SetProperty(ref _ast, value); }
    public SemanticModel? Semantics { get => _semantics; private set => SetProperty(ref _semantics, value); }
    public ISymbolNavigationService? Navigation { get => _navigation; private set => SetProperty(ref _navigation, value); }

    public ImmutableArray<Diagnostic> Diagnostics
    {
        get => _diagnostics;
        private set => SetProperty(ref _diagnostics, value);
    }

    private IReadOnlyList<string> _completionTerms = Array.Empty<string>();
    /// <summary>Editor autocomplete vocabulary: Therion keywords + this file's station/survey names.</summary>
    public IReadOnlyList<string> CompletionTerms
    {
        get => _completionTerms;
        private set => SetProperty(ref _completionTerms, value);
    }

    /// <summary>Replaces text without triggering a binding loop on the editor, then re-parses.</summary>
    public void SetText(string text, bool reparse)
    {
        _documentText = text;
        OnPropertyChanged(nameof(DocumentText));
        if (reparse) Reparse();
    }

    public void RequestScrollTo(SourceSpan span) => ScrollToSpanRequested?.Invoke(this, span);

    private void Reparse()
    {
        var parsed = DocumentParser.Parse(FilePath, _documentText, _commands);
        Ast = parsed.Ast;
        Semantics = parsed.Semantics;
        Navigation = parsed.Navigation;
        Diagnostics = parsed.Diagnostics;
        CompletionTerms = BuildCompletionTerms(parsed.Semantics);
        if (parsed.Semantics is { } model) Measurements.Load(model);
        Reparsed?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<string> BuildCompletionTerms(SemanticModel? model)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in TokenClassifier.Keywords) set.Add(k);
        if (model is not null)
        {
            foreach (var s in model.Stations.Values)
            {
                var qn = s.Name.ToString();
                set.Add(qn);
                int dot = qn.LastIndexOf('.');
                if (dot >= 0 && dot + 1 < qn.Length) set.Add(qn[(dot + 1)..]);
            }
            foreach (var sv in model.Surveys.Values) set.Add(sv.Name.ToString());
        }
        return set.ToList();
    }
}
