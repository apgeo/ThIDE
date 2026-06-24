// Measurements document view (main panel): a filterable / sortable / groupable
// projection of the centreline data legs (shots) AND the stations produced by the
// semantic binder, for a single file. Sorting is handled natively by the DataGrid
// over the DataGridCollectionView; filtering and grouping are driven from this ViewModel.
//
// The grid is editable (#6): Length/Compass/Clino are TwoWay-bound. Two-way binding
// back to the file text is deferred. A toggle switches station columns between the full
// survey-qualified name ("SV-ps3d.R31") and the short station name ("R31") (#5).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Therion.Semantics;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>One measurement (data leg) projected for the Measurements grid; editable (#6).</summary>
public sealed partial class MeasurementRow : ObservableObject
{
    public string Survey { get; init; } = string.Empty;

    // Full ("SV-ps3d.R31") and short ("R31") station names. The visible From/To switch on the
    // panel-wide ShowFullName toggle (#5).
    public string FromFull { get; init; } = string.Empty;
    public string FromShort { get; init; } = string.Empty;
    public string ToFull { get; init; } = string.Empty;
    public string ToShort { get; init; } = string.Empty;

    [ObservableProperty] private bool _showFullName;
    partial void OnShowFullNameChanged(bool value)
    {
        OnPropertyChanged(nameof(From));
        OnPropertyChanged(nameof(To));
    }

    public string From => ShowFullName ? FromFull : FromShort;
    public string To => ShowFullName ? ToFull : ToShort;

    // Editable measurement values (#6). Persistence back to the file text is deferred.
    [ObservableProperty] private double? _length;
    [ObservableProperty] private double? _compass;
    [ObservableProperty] private double? _clino;

    // One column per flag (Therion centreline flags).
    public bool Surface { get; init; }
    public bool Duplicate { get; init; }
    public bool Splay { get; init; }
    public bool Approximate { get; init; }

    /// <summary>Active flags joined for reading / grouping, or "(none)".</summary>
    public string Flags { get; init; } = "(none)";

    /// <summary>Inline and/or leading <c># ...</c> comment, joined with " | ".</summary>
    public string? Comment { get; init; }

    public string Style { get; init; } = string.Empty;
    public int Line { get; init; }
}

/// <summary>One station projected for the Measurements "Stations" sub-panel (read-only).</summary>
public sealed partial class StationMeasurementRow : ObservableObject
{
    public string Survey { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;

    [ObservableProperty] private bool _showFullName;
    partial void OnShowFullNameChanged(bool value) => OnPropertyChanged(nameof(Name));

    public string Name => ShowFullName ? FullName : ShortName;

    public string Kind { get; init; } = string.Empty;
    public int Line { get; init; }
}

public partial class MeasurementsViewModel : ViewModelBase
{
    public const string GroupByNone   = "None";
    public const string GroupBySurvey = "Survey";
    public const string GroupByStyle  = "Style";
    public const string GroupByFlags  = "Flags";
    public const string GroupByKind   = "Kind";

    private readonly List<MeasurementRow> _all = new();
    private readonly List<StationMeasurementRow> _allStations = new();

    // ---- Shots view ----
    [ObservableProperty] private DataGridCollectionView? _view;
    [ObservableProperty] private int _count;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _groupBy = GroupByNone;

    // ---- Stations view ----
    [ObservableProperty] private DataGridCollectionView? _stationsView;
    [ObservableProperty] private int _stationsCount;
    [ObservableProperty] private int _stationsTotalCount;
    [ObservableProperty] private string _stationsFilterText = string.Empty;
    [ObservableProperty] private string _stationsGroupBy = GroupByNone;

    // ---- Shots column visibility ----
    [ObservableProperty] private bool _shotsColSurvey = true;
    [ObservableProperty] private bool _shotsColFrom = true;
    [ObservableProperty] private bool _shotsColTo = true;
    [ObservableProperty] private bool _shotsColLength = true;
    [ObservableProperty] private bool _shotsColCompass = true;
    [ObservableProperty] private bool _shotsColClino = true;
    [ObservableProperty] private bool _shotsColSurface = true;
    [ObservableProperty] private bool _shotsColDuplicate = true;
    [ObservableProperty] private bool _shotsColSplay = true;
    [ObservableProperty] private bool _shotsColApproximate = true;
    [ObservableProperty] private bool _shotsColComment = true;
    [ObservableProperty] private bool _shotsColLine = true;

    // ---- Stations column visibility ----
    [ObservableProperty] private bool _stationsColSurvey = true;
    [ObservableProperty] private bool _stationsColKind = true;
    [ObservableProperty] private bool _stationsColLine = true;

    /// <summary>Show the full survey-qualified station name vs. the short station name (#5, default off).</summary>
    [ObservableProperty] private bool _showFullStationName;

    public IReadOnlyList<string> GroupByOptions { get; } =
        new[] { GroupByNone, GroupBySurvey, GroupByStyle, GroupByFlags };

    public IReadOnlyList<string> StationsGroupByOptions { get; } =
        new[] { GroupByNone, GroupBySurvey, GroupByKind };

    /// <summary>True when this file has at least one shot or station — drives the empty banner (#4).</summary>
    public bool HasAnyData => _all.Count > 0 || _allStations.Count > 0;

    public MeasurementsViewModel()
    {
        LoadColumnSettings();
    }

    // ---- Settings persistence ----

    private void LoadColumnSettings()
    {
        try
        {
            var svc = AppServices.Provider.GetService<IAppSettingsService>();
            if (svc is null) return;
            var s = svc.Current;
            ShotsColSurvey     = s.MColShotsSurvey;
            ShotsColFrom       = s.MColShotsFrom;
            ShotsColTo         = s.MColShotsTo;
            ShotsColLength     = s.MColShotsLength;
            ShotsColCompass    = s.MColShotsCompass;
            ShotsColClino      = s.MColShotsClino;
            ShotsColSurface    = s.MColShotsSurface;
            ShotsColDuplicate  = s.MColShotsDuplicate;
            ShotsColSplay      = s.MColShotsSplay;
            ShotsColApproximate = s.MColShotsApproximate;
            ShotsColComment    = s.MColShotsComment;
            ShotsColLine       = s.MColShotsLine;
            StationsColSurvey  = s.MColStationsSurvey;
            StationsColKind    = s.MColStationsKind;
            StationsColLine    = s.MColStationsLine;
        }
        catch { }
    }

    private void SaveColumnSettings()
    {
        try
        {
            var svc = AppServices.Provider.GetService<IAppSettingsService>();
            if (svc is null) return;
            svc.Save(svc.Current with
            {
                MColShotsSurvey     = ShotsColSurvey,
                MColShotsFrom       = ShotsColFrom,
                MColShotsTo         = ShotsColTo,
                MColShotsLength     = ShotsColLength,
                MColShotsCompass    = ShotsColCompass,
                MColShotsClino      = ShotsColClino,
                MColShotsSurface    = ShotsColSurface,
                MColShotsDuplicate  = ShotsColDuplicate,
                MColShotsSplay      = ShotsColSplay,
                MColShotsApproximate = ShotsColApproximate,
                MColShotsComment    = ShotsColComment,
                MColShotsLine       = ShotsColLine,
                MColStationsSurvey  = StationsColSurvey,
                MColStationsKind    = StationsColKind,
                MColStationsLine    = StationsColLine,
            });
        }
        catch { }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name &&
            (name.StartsWith("ShotsCol", StringComparison.Ordinal) ||
             name.StartsWith("StationsCol", StringComparison.Ordinal)))
            SaveColumnSettings();
    }

    // ---- Data loading ----

    /// <summary>Loads measurements + stations from a single-file semantic model.</summary>
    public void Load(SemanticModel model)
    {
        BuildStations(ProjectStations(model.Stations.Values));
        Build(Project(model.Shots));
    }

    /// <summary>Loads measurements + stations from every per-file model in a workspace snapshot.</summary>
    public void Load(WorkspaceSemanticModel workspace)
    {
        BuildStations(workspace.PerFile.Values.SelectMany(m => ProjectStations(m.Stations.Values)));
        Build(workspace.PerFile.Values.SelectMany(m => Project(m.Shots)));
    }

    // ---- Shots filter / group ----

    partial void OnShowFullStationNameChanged(bool value)
    {
        foreach (var r in _all) r.ShowFullName = value;
        foreach (var s in _allStations) s.ShowFullName = value;
    }

    partial void OnFilterTextChanged(string value)
    {
        View?.Refresh();
        UpdateCount();
    }

    partial void OnGroupByChanged(string value) => ApplyGrouping();

    // ---- Stations filter / group ----

    partial void OnStationsFilterTextChanged(string value)
    {
        StationsView?.Refresh();
        UpdateStationsCount();
    }

    partial void OnStationsGroupByChanged(string value) => ApplyStationsGrouping();

    // ---- Projection ----

    private IEnumerable<MeasurementRow> Project(ImmutableArray<ShotSymbol> shots)
    {
        foreach (var s in shots)
        {
            var f = s.Flags;
            yield return new MeasurementRow
            {
                Survey      = s.From.HasParent ? s.From.Parent().ToString() : string.Empty,
                FromFull    = s.From.ToString(),
                FromShort   = s.From.Last,
                ToFull      = s.To.ToString(),
                ToShort     = s.To.Last,
                ShowFullName = ShowFullStationName,
                Length      = s.Length,
                Compass     = s.Compass,
                Clino       = s.Clino,
                Surface     = f.HasFlag(ShotFlags.Surface),
                Duplicate   = f.HasFlag(ShotFlags.Duplicate),
                Splay       = f.HasFlag(ShotFlags.Splay),
                Approximate = f.HasFlag(ShotFlags.Approximate),
                Flags       = FlagText(f),
                Comment     = s.Comment,
                Style       = s.FieldDefinition?.Style ?? string.Empty,
                Line        = s.Span.Start.Line,
            };
        }
    }

    private IEnumerable<StationMeasurementRow> ProjectStations(IEnumerable<StationSymbol> stations) =>
        stations
            .Select(s => new StationMeasurementRow
            {
                Survey    = s.Name.HasParent ? s.Name.Parent().ToString() : string.Empty,
                FullName  = s.Name.ToString(),
                ShortName = s.Name.Last,
                ShowFullName = ShowFullStationName,
                Kind      = s.Kind.ToString(),
                Line      = s.DeclarationSpan.Start.Line,
            })
            .OrderBy(r => r.FullName, StringComparer.Ordinal);

    // ---- Build / grouping / filtering ----

    private void Build(IEnumerable<MeasurementRow> rows)
    {
        _all.Clear();
        _all.AddRange(rows);

        var view = new DataGridCollectionView(_all) { Filter = FilterRow };
        View = view;
        ApplyGrouping();
        TotalCount = _all.Count;
        UpdateCount();
        OnPropertyChanged(nameof(HasAnyData));
    }

    private void BuildStations(IEnumerable<StationMeasurementRow> rows)
    {
        _allStations.Clear();
        _allStations.AddRange(rows);

        var view = new DataGridCollectionView(_allStations) { Filter = FilterStation };
        StationsView = view;
        ApplyStationsGrouping();
        StationsTotalCount = _allStations.Count;
        UpdateStationsCount();
        OnPropertyChanged(nameof(HasAnyData));
    }

    private void ApplyGrouping()
    {
        if (View is null) return;
        using (View.DeferRefresh())
        {
            View.GroupDescriptions.Clear();
            var prop = GroupBy switch
            {
                GroupBySurvey => nameof(MeasurementRow.Survey),
                GroupByStyle  => nameof(MeasurementRow.Style),
                GroupByFlags  => nameof(MeasurementRow.Flags),
                _             => null,
            };
            if (prop is not null)
                View.GroupDescriptions.Add(new DataGridPathGroupDescription(prop));
        }
        UpdateCount();
    }

    private void ApplyStationsGrouping()
    {
        if (StationsView is null) return;
        using (StationsView.DeferRefresh())
        {
            StationsView.GroupDescriptions.Clear();
            var prop = StationsGroupBy switch
            {
                GroupBySurvey => nameof(StationMeasurementRow.Survey),
                GroupByKind   => nameof(StationMeasurementRow.Kind),
                _             => null,
            };
            if (prop is not null)
                StationsView.GroupDescriptions.Add(new DataGridPathGroupDescription(prop));
        }
        UpdateStationsCount();
    }

    private bool FilterRow(object o)
    {
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        if (o is not MeasurementRow r) return true;
        var q = FilterText.Trim();
        // Match on the full qualified names so the filter behaves the same regardless of the
        // short/full display toggle (#5).
        return Contains(r.FromFull, q) || Contains(r.ToFull, q) || Contains(r.Survey, q)
            || Contains(r.Flags, q) || Contains(r.Comment, q) || Contains(r.Style, q);
    }

    private bool FilterStation(object o)
    {
        if (string.IsNullOrWhiteSpace(StationsFilterText)) return true;
        if (o is not StationMeasurementRow r) return true;
        var q = StationsFilterText.Trim();
        return Contains(r.FullName, q) || Contains(r.ShortName, q)
            || Contains(r.Survey, q) || Contains(r.Kind, q);
    }

    private void UpdateCount() =>
        Count = string.IsNullOrWhiteSpace(FilterText) ? _all.Count : _all.Count(FilterRow);

    private void UpdateStationsCount() =>
        StationsCount = string.IsNullOrWhiteSpace(StationsFilterText)
            ? _allStations.Count
            : _allStations.Count(FilterStation);

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string FlagText(ShotFlags f)
    {
        if (f == ShotFlags.None) return "(none)";
        var parts = new List<string>(4);
        if (f.HasFlag(ShotFlags.Surface))     parts.Add("surface");
        if (f.HasFlag(ShotFlags.Duplicate))   parts.Add("duplicate");
        if (f.HasFlag(ShotFlags.Splay))       parts.Add("splay");
        if (f.HasFlag(ShotFlags.Approximate)) parts.Add("approx");
        return string.Join(", ", parts);
    }
}
