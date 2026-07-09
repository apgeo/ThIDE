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
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

/// <summary>One row per station in the Object Browser. <see cref="File"/> is the full path.</summary>
public sealed record StationRow(string QualifiedName, string Kind, string Survey, string File, int Line)
    : IBrowserNavRow
{
    public string? NavFile => File;
    public int NavLine => Line;
}

/// <summary>A row that can navigate to source (click-to-source).</summary>
public interface IBrowserNavRow
{
    string? NavFile { get; }
    int NavLine { get; }
}

// entity rows for the additional Object Browser tabs. Each carries its
// declaration <see cref="SourceSpan"/> so a double-click can jump to source.
public sealed record SurveyEntityRow(string Name, string Title, string Parent, SourceSpan Span,
    SurveySymbol? Source = null) : IBrowserNavRow
{
    public string File => System.IO.Path.GetFileName(Span.FilePath ?? string.Empty);
    public int Line => Span.Start.Line;
    public string? NavFile => Span.FilePath;
    public int NavLine => Span.Start.Line;
}
public sealed record FixEntityRow(string Station, string Coordinates, string Cs, SourceSpan Span) : IBrowserNavRow
{
    public string File => System.IO.Path.GetFileName(Span.FilePath ?? string.Empty);
    public int Line => Span.Start.Line;
    public string? NavFile => Span.FilePath;
    public int NavLine => Span.Start.Line;
}
public sealed record EquateEntityRow(string Stations, SourceSpan Span) : IBrowserNavRow
{
    public string File => System.IO.Path.GetFileName(Span.FilePath ?? string.Empty);
    public int Line => Span.Start.Line;
    public string? NavFile => Span.FilePath;
    public int NavLine => Span.Start.Line;
}
public sealed record ScrapEntityRow(string Id, string Sketch, SourceSpan Span) : IBrowserNavRow
{
    public string File => System.IO.Path.GetFileName(Span.FilePath ?? string.Empty);
    public int Line => Span.Start.Line;
    public string? NavFile => Span.FilePath;
    public int NavLine => Span.Start.Line;
}
public sealed record MapEntityRow(string Id, string Title, string Projection, int Members, SourceSpan Span) : IBrowserNavRow
{
    public string File => System.IO.Path.GetFileName(Span.FilePath ?? string.Empty);
    public int Line => Span.Start.Line;
    public string? NavFile => Span.FilePath;
    public int NavLine => Span.Start.Line;
}
public sealed record Th2EntityRow(string Type, string Scrap, SourceSpan Span) : IBrowserNavRow
{
    public string File => System.IO.Path.GetFileName(Span.FilePath ?? string.Empty);
    public int Line => Span.Start.Line;
    public string? NavFile => Span.FilePath;
    public int NavLine => Span.Start.Line;
}

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
    /// <summary>The semantic shot this row projects; used by drill-down filters (null for sample rows).</summary>
    public ShotSymbol? Source { get; }

    [ObservableProperty] private double? _length;
    [ObservableProperty] private double? _compass;
    [ObservableProperty] private double? _clino;

    public ShotRow(string from, string to, double? length, double? compass, double? clino, int line,
        DataRow? sourceRow = null, DataCommand? fieldDefinition = null, ShotSymbol? source = null)
    {
        From = from;
        To = to;
        _length = length;
        _compass = compass;
        _clino = clino;
        Line = line;
        SourceRow = sourceRow;
        FieldDefinition = fieldDefinition;
        Source = source;
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

    // entity collections for the additional tabs.
    [ObservableProperty] private IReadOnlyList<SurveyEntityRow> _surveys = System.Array.Empty<SurveyEntityRow>();
    [ObservableProperty] private IReadOnlyList<FixEntityRow> _fixes = System.Array.Empty<FixEntityRow>();
    [ObservableProperty] private IReadOnlyList<EquateEntityRow> _equates = System.Array.Empty<EquateEntityRow>();
    [ObservableProperty] private IReadOnlyList<ScrapEntityRow> _scraps = System.Array.Empty<ScrapEntityRow>();
    [ObservableProperty] private IReadOnlyList<MapEntityRow> _maps = System.Array.Empty<MapEntityRow>();
    [ObservableProperty] private IReadOnlyList<Th2EntityRow> _points = System.Array.Empty<Th2EntityRow>();
    [ObservableProperty] private IReadOnlyList<Th2EntityRow> _lines = System.Array.Empty<Th2EntityRow>();
    [ObservableProperty] private IReadOnlyList<Th2EntityRow> _areas = System.Array.Empty<Th2EntityRow>();

    // ---- per-tab filtering (free-text box + optional custom subset filter) ----
    // One BrowserTabFilter per tab, in tab-strip order (== BrowserTab). The View binds each grid to
    // <Tab>Filter.Items and the shared filter bar to ActiveFilter.
    public BrowserTabFilter StationsFilter { get; } = new(BrowserTab.Stations);
    public BrowserTabFilter ShotsFilter    { get; } = new(BrowserTab.Shots);
    public BrowserTabFilter SurveysFilter  { get; } = new(BrowserTab.Surveys);
    public BrowserTabFilter FixesFilter    { get; } = new(BrowserTab.Fixes);
    public BrowserTabFilter EquatesFilter  { get; } = new(BrowserTab.Equates);
    public BrowserTabFilter MapsFilter     { get; } = new(BrowserTab.Maps);
    public BrowserTabFilter ScrapsFilter   { get; } = new(BrowserTab.Scraps);
    public BrowserTabFilter PointsFilter   { get; } = new(BrowserTab.Points);
    public BrowserTabFilter LinesFilter    { get; } = new(BrowserTab.Lines);
    public BrowserTabFilter AreasFilter    { get; } = new(BrowserTab.Areas);

    private BrowserTabFilter[]? _filters;
    private BrowserTabFilter[] Filters => _filters ??= new[]
    {
        StationsFilter, ShotsFilter, SurveysFilter, FixesFilter, EquatesFilter,
        MapsFilter, ScrapsFilter, PointsFilter, LinesFilter, AreasFilter,
    };

    /// <summary>Bound to the TabControl's selected index; also drives <see cref="ActiveFilter"/>.</summary>
    [ObservableProperty] private int _selectedTabIndex;

    /// <summary>The filter for the currently selected tab (what the shared filter bar edits).</summary>
    public BrowserTabFilter ActiveFilter => Filters[System.Math.Clamp(SelectedTabIndex, 0, Filters.Length - 1)];

    partial void OnSelectedTabIndexChanged(int value) => OnPropertyChanged(nameof(ActiveFilter));

    // Keep each tab filter's backing rows in sync as the browser reloads (the filters survive reload).
    partial void OnStationsChanged(IReadOnlyList<StationRow> value) => StationsFilter.SetSource(value);
    partial void OnShotsChanged(IReadOnlyList<ShotRow> value) => ShotsFilter.SetSource(value);
    partial void OnSurveysChanged(IReadOnlyList<SurveyEntityRow> value) => SurveysFilter.SetSource(value);
    partial void OnFixesChanged(IReadOnlyList<FixEntityRow> value) => FixesFilter.SetSource(value);
    partial void OnEquatesChanged(IReadOnlyList<EquateEntityRow> value) => EquatesFilter.SetSource(value);
    partial void OnMapsChanged(IReadOnlyList<MapEntityRow> value) => MapsFilter.SetSource(value);
    partial void OnScrapsChanged(IReadOnlyList<ScrapEntityRow> value) => ScrapsFilter.SetSource(value);
    partial void OnPointsChanged(IReadOnlyList<Th2EntityRow> value) => PointsFilter.SetSource(value);
    partial void OnLinesChanged(IReadOnlyList<Th2EntityRow> value) => LinesFilter.SetSource(value);
    partial void OnAreasChanged(IReadOnlyList<Th2EntityRow> value) => AreasFilter.SetSource(value);

    /// <summary>
    /// Applies a custom subset filter (e.g. an Overview ▸ Quality drill-down): switches to the target
    /// tab and restricts it to the predicate's rows with a labelled chip. Generic, so any caller can
    /// push any arbitrary subset of a tab's rows.
    /// </summary>
    public void ApplyFilter(BrowserFilter filter)
    {
        if (filter is null) return;
        int idx = System.Math.Clamp((int)filter.Tab, 0, Filters.Length - 1);
        SelectedTabIndex = idx;
        Filters[idx].ApplyCustom(filter.Label, filter.Predicate);
    }

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
    private readonly IDocumentService? _documents;

    public ObjectBrowserViewModel() { }

    public ObjectBrowserViewModel(IStringLocalizer<Strings> localizer, ILanguageService language,
        IAppSettingsService? settings = null, IDocumentService? documents = null)
    {
        _l = localizer;
        _settings = settings;
        _documents = documents;
        language.LanguageChanged += (_, _) => RaiseHeadersChanged();
    }

    private bool EntitiesEnabled => _settings?.Current.EnableObjectBrowserEntities ?? true;

    /// <summary>jump to a row's declaration in source.</summary>
    public void NavigateTo(IBrowserNavRow? row)
    {
        if (row is null || string.IsNullOrEmpty(row.NavFile) || _documents is null) return;
        var span = new SourceSpan(row.NavFile!,
            new SourceLocation(row.NavLine, 1), new SourceLocation(row.NavLine, 1), 0, 1);
        _ = _documents.NavigateToSpanAsync(span);
    }

    // ----- : identifier actions from any object grid row --------------

    /// <summary>The identifier and reference-kind for a row, or (null, null) when it has none.</summary>
    private static (string? Name, Therion.Processing.Abstractions.ReferenceKind? Kind) NameAndKind(IBrowserNavRow? row) => row switch
    {
        StationRow s      => (s.QualifiedName, Therion.Processing.Abstractions.ReferenceKind.Station),
        SurveyEntityRow v => (v.Name,          Therion.Processing.Abstractions.ReferenceKind.Survey),
        ScrapEntityRow sc => (sc.Id,           null),
        MapEntityRow m    => (m.Id,            null),
        _                 => (null, null),
    };

    /// <summary>True when a row carries a renamable identifier (station/survey) — gates the menu item.</summary>
    public static bool CanRename(IBrowserNavRow? row) => NameAndKind(row).Kind is not null;

    /// <summary>find every reference to the row's identifier across the project.</summary>
    public void FindReferences(IBrowserNavRow? row)
    {
        var (name, _) = NameAndKind(row);
        if (!string.IsNullOrEmpty(name)) _documents?.RequestFindReferences(name!);
    }

    /// <summary>start a project-wide rename of the row's identifier (station/survey only).</summary>
    public void RenameSymbol(IBrowserNavRow? row)
    {
        var (name, kind) = NameAndKind(row);
        if (!string.IsNullOrEmpty(name) && kind is { } k) _documents?.RequestRenameSymbol(name!, k);
    }

    /// <summary>copy the row's qualified identifier to the clipboard.</summary>
    public void CopyQualifiedName(IBrowserNavRow? row)
    {
        var (name, _) = NameAndKind(row);
        if (!string.IsNullOrEmpty(name)) ClipboardHelper.SetText(name!);
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
                s.Span.Start.Line, s.SourceRow, s.FieldDefinition, s);
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
                    s.Span.Start.Line, s.SourceRow, s.FieldDefinition, s);
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

    // ---- : entity tabs --------------------------------------------

    private static string Coords(StationSymbol s) =>
        s.FixX is not null && s.FixY is not null
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1:0.##} {2:0.##}", s.FixX, s.FixY, s.FixZ ?? 0)
            : "—";

    private void LoadEntities(SemanticModel model)
    {
        if (!EntitiesEnabled) { ClearEntities(); return; }
        Surveys = model.Surveys.Values
            .Select(sv => new SurveyEntityRow(sv.Name.ToString(), sv.Title ?? string.Empty,
                sv.Name.HasParent ? sv.Name.Parent().ToString() : string.Empty, sv.DeclarationSpan, sv))
            .OrderBy(r => r.Name, System.StringComparer.Ordinal).ToList();
        Fixes = model.Stations.Values.Where(s => s.Kind == StationDeclarationKind.Fix)
            .Select(s => new FixEntityRow(s.Name.ToString(), Coords(s), s.Cs ?? string.Empty, s.DeclarationSpan))
            .OrderBy(r => r.Station, System.StringComparer.Ordinal).ToList();
        Equates = model.EquateRecords
            .Select(e => new EquateEntityRow(string.Join(" = ", e.Stations), e.Span)).ToList();
        Maps = model.Maps.Values
            .Select(m => new MapEntityRow(m.Id, m.Title ?? string.Empty, m.Projection ?? string.Empty,
                m.Members.Length, m.DeclarationSpan))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        Scraps = model.Scraps.Values
            .Select(s => new ScrapEntityRow(s.Id, string.Empty, s.DeclarationSpan))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        Points = Lines = Areas = System.Array.Empty<Th2EntityRow>(); // .th2 objects only at workspace scope
    }

    private void LoadEntities(WorkspaceSemanticModel ws)
    {
        if (!EntitiesEnabled) { ClearEntities(); return; }
        Surveys = ws.SurveysByFullName.Values
            .Select(sv => new SurveyEntityRow(sv.Name.ToString(), sv.Title ?? string.Empty,
                sv.Name.HasParent ? sv.Name.Parent().ToString() : string.Empty, sv.DeclarationSpan, sv))
            .OrderBy(r => r.Name, System.StringComparer.Ordinal).ToList();
        Fixes = ws.StationsByQn.Values.Where(s => s.Kind == StationDeclarationKind.Fix)
            .Select(s => new FixEntityRow(s.Name.ToString(), Coords(s), s.Cs ?? string.Empty, s.DeclarationSpan))
            .OrderBy(r => r.Station, System.StringComparer.Ordinal).ToList();
        Equates = ws.PerFile.Values.SelectMany(m => m.EquateRecords)
            .Select(e => new EquateEntityRow(string.Join(" = ", e.Stations), e.Span)).ToList();
        Maps = ws.MapsById.Values
            .Select(m => new MapEntityRow(m.Id, m.Title ?? string.Empty, m.Projection ?? string.Empty,
                m.Members.Length, m.DeclarationSpan))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        // which .xvi each scrap traces — from the .th2 → .xvi file-graph edges.
        var sketchByTh2 = BuildSketchMap(ws);
        Scraps = ws.ScrapsById.Values
            .Select(s => new ScrapEntityRow(s.Id,
                sketchByTh2.TryGetValue(s.DeclarationSpan.FilePath ?? string.Empty, out var sk) ? sk : string.Empty,
                s.DeclarationSpan))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal).ToList();
        Th2EntityRow Row(Th2ObjectRecord o) => new(o.Type, o.ScrapId, o.Span);
        Points = ws.Th2Objects.Where(o => o.Kind == "point").Select(Row).ToList();
        Lines  = ws.Th2Objects.Where(o => o.Kind == "line").Select(Row).ToList();
        Areas  = ws.Th2Objects.Where(o => o.Kind == "area").Select(Row).ToList();
    }

    // Maps each .th2 file to the comma-joined .xvi file names it sketches (scrap→xvi).
    private static Dictionary<string, string> BuildSketchMap(WorkspaceSemanticModel ws)
    {
        var byTh2 = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to, _) in ws.FileGraphEdges)
        {
            if (!to.EndsWith(".xvi", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!byTh2.TryGetValue(from, out var set))
                byTh2[from] = set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            set.Add(System.IO.Path.GetFileName(to));
        }
        return byTh2.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value.OrderBy(x => x)),
            System.StringComparer.OrdinalIgnoreCase);
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
