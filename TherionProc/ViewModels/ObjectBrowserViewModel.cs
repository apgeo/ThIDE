// Implementation Plan �7.3 � Object Browser ViewModel.
// Flat row-list projections of the semantic model so a virtualized DataGrid
// can scroll smoothly through 20k+ legs (�7.4).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Resources;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>One row per station in the Object Browser.</summary>
public sealed record StationRow(string QualifiedName, string Kind, string Survey, string File, int Line);

/// <summary>
/// One row per shot (data leg). Editable: Length / Compass / Clino edits are
/// routed through <see cref="IModelEditService"/> and persisted by the host
/// via <see cref="ShotEditRequested"/> (Plan �7.3 / M6 #8).
/// </summary>
public sealed partial class ShotRow : ObservableObject
{
    private bool _suppress;
    public event EventHandler<ShotEditEventArgs>? EditRequested;

    public string From { get; }
    public string To { get; }
    public int Line { get; }
    /// <summary>Backing AST nodes; null for the sample/designer rows.</summary>
    public DataRow? SourceRow { get; }
    public DataCommand? FieldDefinition { get; }

    [ObservableProperty] private double? _length;
    [ObservableProperty] private double? _compass;
    [ObservableProperty] private double? _clino;

    public ShotRow(string from, string to, double? length, double? compass, double? clino, int line,
        DataRow? sourceRow = null, DataCommand? fieldDefinition = null)
    {
        From = from;
        To = to;
        _length = length;
        _compass = compass;
        _clino = clino;
        Line = line;
        SourceRow = sourceRow;
        FieldDefinition = fieldDefinition;
    }

    partial void OnLengthChanged(double? value)  { Raise("length",  value); }
    partial void OnCompassChanged(double? value) { Raise("compass", value); }
    partial void OnClinoChanged(double? value)   { Raise("clino",   value); }

    private void Raise(string field, double? value)
    {
        if (_suppress) return;
        if (SourceRow is null || FieldDefinition is null) return;
        EditRequested?.Invoke(this, new ShotEditEventArgs(this, field, value));
    }

    internal void SetSilently(double? length, double? compass, double? clino)
    {
        _suppress = true;
        Length = length; Compass = compass; Clino = clino;
        _suppress = false;
    }
}

public sealed class ShotEditEventArgs : EventArgs
{
    public ShotEditEventArgs(ShotRow row, string field, double? value) { Row = row; Field = field; Value = value; }
    public ShotRow Row { get; }
    public string Field { get; }
    public double? Value { get; }
}

public partial class ObjectBrowserViewModel : ViewModelBase
{
    private readonly IStringLocalizer<Strings>? _l;

    /// <summary>Raised when the user edits a shot cell; host applies via IModelEditService.</summary>
    public event EventHandler<ShotEditEventArgs>? ShotEditRequested;

    [ObservableProperty]
    private IReadOnlyList<StationRow> _stations = System.Array.Empty<StationRow>();

    [ObservableProperty]
    private IReadOnlyList<ShotRow> _shots = System.Array.Empty<ShotRow>();

    [ObservableProperty]
    private int _stationCount;

    [ObservableProperty]
    private int _shotCount;

    // Localized labels � bound directly so language switch refreshes headers.
    public string TabStations => L("Browser_Tab_Stations", "Stations");
    public string TabShots    => L("Browser_Tab_Shots",    "Shots");
    public string ColQualifiedName => L("Browser_Col_QualifiedName", "Qualified name");
    public string ColKind    => L("Browser_Col_Kind",    "Kind");
    public string ColSurvey  => L("Browser_Col_Survey",  "Survey");
    public string ColLine    => L("Browser_Col_Line",    "Line");
    public string ColFrom    => L("Browser_Col_From",    "From");
    public string ColTo      => L("Browser_Col_To",      "To");
    public string ColLength  => L("Browser_Col_Length",  "Length");
    public string ColCompass => L("Browser_Col_Compass", "Compass");
    public string ColClino   => L("Browser_Col_Clino",   "Clino");

    public ObjectBrowserViewModel() { }

    public ObjectBrowserViewModel(IStringLocalizer<Strings> localizer, ILanguageService language)
    {
        _l = localizer;
        language.LanguageChanged += (_, _) => RaiseHeadersChanged();
    }

    private string L(string key, string fallback)
    {
        if (_l is null) return fallback;
        var v = _l[key];
        return v.ResourceNotFound ? fallback : v.Value;
    }

    private void RaiseHeadersChanged()
    {
        OnPropertyChanged(nameof(TabStations));
        OnPropertyChanged(nameof(TabShots));
        OnPropertyChanged(nameof(ColQualifiedName));
        OnPropertyChanged(nameof(ColKind));
        OnPropertyChanged(nameof(ColSurvey));
        OnPropertyChanged(nameof(ColLine));
        OnPropertyChanged(nameof(ColFrom));
        OnPropertyChanged(nameof(ColTo));
        OnPropertyChanged(nameof(ColLength));
        OnPropertyChanged(nameof(ColCompass));
        OnPropertyChanged(nameof(ColClino));
    }

    public void Load(SemanticModel model)
    {
        var stations = model.Stations.Values
            .Select(s => new StationRow(
                s.Name.ToString(),
                s.Kind.ToString(),
                s.Name.HasParent ? s.Name.Parent().ToString() : string.Empty,
                s.DeclarationSpan.FilePath,
                s.DeclarationSpan.Start.Line))
            .OrderBy(r => r.QualifiedName, System.StringComparer.Ordinal)
            .ToList();

        var shots = new List<ShotRow>(model.Shots.Length);
        foreach (var s in model.Shots)
        {
            var row = new ShotRow(s.From.ToString(), s.To.ToString(), s.Length, s.Compass, s.Clino,
                s.Span.Start.Line, s.SourceRow, s.FieldDefinition);
            row.EditRequested += OnRowEdit;
            shots.Add(row);
        }

        Stations = stations;
        Shots = shots;
        StationCount = stations.Count;
        ShotCount = shots.Count;
    }

    /// <summary>
    /// Loads stations and shots from all per-file models in a workspace snapshot.
    /// Used when "Open Folder…" resolves to a workspace (the entry file is typically
    /// a thconfig with no survey data of its own).
    /// </summary>
    public void Load(Therion.Semantics.WorkspaceSemanticModel workspace)
    {
        var seenStations = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        var stations = new List<StationRow>();
        var shots = new List<ShotRow>();

        foreach (var model in workspace.PerFile.Values)
        {
            foreach (var s in model.Stations.Values)
            {
                var key = s.Name.ToString();
                if (!seenStations.Add(key)) continue;
                stations.Add(new StationRow(
                    key,
                    s.Kind.ToString(),
                    s.Name.HasParent ? s.Name.Parent().ToString() : string.Empty,
                    s.DeclarationSpan.FilePath,
                    s.DeclarationSpan.Start.Line));
            }

            foreach (var s in model.Shots)
            {
                var row = new ShotRow(s.From.ToString(), s.To.ToString(), s.Length, s.Compass, s.Clino,
                    s.Span.Start.Line, s.SourceRow, s.FieldDefinition);
                row.EditRequested += OnRowEdit;
                shots.Add(row);
            }
        }

        stations.Sort((a, b) => System.StringComparer.Ordinal.Compare(a.QualifiedName, b.QualifiedName));

        Stations = stations;
        Shots = shots;
        StationCount = stations.Count;
        ShotCount = shots.Count;
    }

    private void OnRowEdit(object? sender, ShotEditEventArgs e) => ShotEditRequested?.Invoke(this, e);
}
