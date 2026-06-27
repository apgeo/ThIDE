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
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Resources;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>One row per station in the Object Browser.</summary>
public sealed record StationRow(string QualifiedName, string Kind, string Survey, string File, int Line);

// DATA-03 — entity rows for the additional Object Browser tabs.
public sealed record SurveyEntityRow(string Name, string Title, string Parent, string File, int Line);
public sealed record FixEntityRow(string Station, string Coordinates, string Cs, string File, int Line);
public sealed record EquateEntityRow(string Stations, string File, int Line);
public sealed record ScrapEntityRow(string Id, string File, int Line);
public sealed record MapEntityRow(string Id, string Title, string Projection, int Members, string File, int Line);
public sealed record Th2EntityRow(string Type, string Scrap, string File, int Line);

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

    // DATA-03 — entity collections for the additional tabs.
    [ObservableProperty] private IReadOnlyList<SurveyEntityRow> _surveys = System.Array.Empty<SurveyEntityRow>();
    [ObservableProperty] private IReadOnlyList<FixEntityRow> _fixes = System.Array.Empty<FixEntityRow>();
    [ObservableProperty] private IReadOnlyList<EquateEntityRow> _equates = System.Array.Empty<EquateEntityRow>();
    [ObservableProperty] private IReadOnlyList<ScrapEntityRow> _scraps = System.Array.Empty<ScrapEntityRow>();
    [ObservableProperty] private IReadOnlyList<MapEntityRow> _maps = System.Array.Empty<MapEntityRow>();
    [ObservableProperty] private IReadOnlyList<Th2EntityRow> _points = System.Array.Empty<Th2EntityRow>();
    [ObservableProperty] private IReadOnlyList<Th2EntityRow> _lines = System.Array.Empty<Th2EntityRow>();
    [ObservableProperty] private IReadOnlyList<Th2EntityRow> _areas = System.Array.Empty<Th2EntityRow>();

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

    private readonly IAppSettingsService? _settings;

    public ObjectBrowserViewModel() { }

    public ObjectBrowserViewModel(IStringLocalizer<Strings> localizer, ILanguageService language,
        IAppSettingsService? settings = null)
    {
        _l = localizer;
        _settings = settings;
        language.LanguageChanged += (_, _) => RaiseHeadersChanged();
    }

    private bool EntitiesEnabled => _settings?.Current.EnableObjectBrowserEntities ?? true;

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
        LoadEntities(model);
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
        LoadEntities(workspace);
    }

    private void OnRowEdit(object? sender, ShotEditEventArgs e) => ShotEditRequested?.Invoke(this, e);

    // ---- DATA-03: entity tabs --------------------------------------------

    private static string FileName(SourceSpan s) => System.IO.Path.GetFileName(s.FilePath ?? string.Empty);
    private static string Coords(StationSymbol s) =>
        s.FixX is not null && s.FixY is not null
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##}", s.FixX, s.FixY, s.FixZ ?? 0)
            : "—";

    private void LoadEntities(SemanticModel model)
    {
        if (!EntitiesEnabled) { ClearEntities(); return; }
        Surveys = model.Surveys.Values
            .Select(sv => new SurveyEntityRow(sv.Name.ToString(), sv.Title ?? string.Empty,
                sv.Name.HasParent ? sv.Name.Parent().ToString() : string.Empty,
                FileName(sv.DeclarationSpan), sv.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Name, System.StringComparer.Ordinal).ToList();
        Fixes = model.Stations.Values.Where(s => s.Kind == StationDeclarationKind.Fix)
            .Select(s => new FixEntityRow(s.Name.ToString(), Coords(s), s.Cs ?? string.Empty,
                FileName(s.DeclarationSpan), s.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Station, System.StringComparer.Ordinal).ToList();
        Equates = model.EquateRecords
            .Select(e => new EquateEntityRow(string.Join(" = ", e.Stations), FileName(e.Span), e.Span.Start.Line))
            .ToList();
        Maps = model.Maps.Values
            .Select(m => new MapEntityRow(m.Id, m.Title ?? string.Empty, m.Projection ?? string.Empty,
                m.Members.Length, FileName(m.DeclarationSpan), m.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        Scraps = model.Scraps.Values
            .Select(s => new ScrapEntityRow(s.Id, FileName(s.DeclarationSpan), s.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        Points = Lines = Areas = System.Array.Empty<Th2EntityRow>(); // .th2 objects only at workspace scope
    }

    private void LoadEntities(WorkspaceSemanticModel ws)
    {
        if (!EntitiesEnabled) { ClearEntities(); return; }
        Surveys = ws.SurveysByFullName.Values
            .Select(sv => new SurveyEntityRow(sv.Name.ToString(), sv.Title ?? string.Empty,
                sv.Name.HasParent ? sv.Name.Parent().ToString() : string.Empty,
                FileName(sv.DeclarationSpan), sv.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Name, System.StringComparer.Ordinal).ToList();
        Fixes = ws.StationsByQn.Values.Where(s => s.Kind == StationDeclarationKind.Fix)
            .Select(s => new FixEntityRow(s.Name.ToString(), Coords(s), s.Cs ?? string.Empty,
                FileName(s.DeclarationSpan), s.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Station, System.StringComparer.Ordinal).ToList();
        Equates = ws.PerFile.Values.SelectMany(m => m.EquateRecords)
            .Select(e => new EquateEntityRow(string.Join(" = ", e.Stations), FileName(e.Span), e.Span.Start.Line))
            .ToList();
        Maps = ws.MapsById.Values
            .Select(m => new MapEntityRow(m.Id, m.Title ?? string.Empty, m.Projection ?? string.Empty,
                m.Members.Length, FileName(m.DeclarationSpan), m.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        Scraps = ws.ScrapsById.Values
            .Select(s => new ScrapEntityRow(s.Id, FileName(s.DeclarationSpan), s.DeclarationSpan.Start.Line))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        Th2EntityRow Row(Th2ObjectRecord o) => new(o.Type, o.ScrapId, FileName(o.Span), o.Span.Start.Line);
        Points = ws.Th2Objects.Where(o => o.Kind == "point").Select(Row).ToList();
        Lines  = ws.Th2Objects.Where(o => o.Kind == "line").Select(Row).ToList();
        Areas  = ws.Th2Objects.Where(o => o.Kind == "area").Select(Row).ToList();
    }

    private void ClearEntities()
    {
        Surveys = System.Array.Empty<SurveyEntityRow>();
        Fixes = System.Array.Empty<FixEntityRow>();
        Equates = System.Array.Empty<EquateEntityRow>();
        Scraps = System.Array.Empty<ScrapEntityRow>();
        Maps = System.Array.Empty<MapEntityRow>();
        Points = Lines = Areas = System.Array.Empty<Th2EntityRow>();
    }
}
