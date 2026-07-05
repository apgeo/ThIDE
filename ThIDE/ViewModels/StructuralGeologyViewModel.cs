// Phase 3 — content view-model for the Structural Geology dock tool.
//
// Drives the tab wizard (Detect → Measurements → Resulted Planes → Plot). Pulls the active file's
// (or whole project's) SemanticModel from IDocumentService, runs the pure Therion.Structural pipeline
// (detect → fit → declination), and exposes the raw measurements (with include checkboxes) + resulted
// planes as grid rows. Toggling a measurement's inclusion recomputes only its batch's plane, live.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Semantics;
using Therion.Structural;
using Therion.Syntax;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public sealed partial class StructuralGeologyViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;
    private readonly IWorkspaceSession? _session;
    private readonly IStructuralPlotAssetHost? _plotHost;
    private readonly IAppSettingsService? _settings;

    private const int TabCount = 4;

    private bool _activated;       // don't analyse until the panel is first shown
    private bool _suspend;         // batch option changes during a programmatic reset / settings load
    private bool _bulk;            // suppress per-row recompute during a check-all / per-station toggle
    private bool _loaded;          // persisted state applied → persist subsequent changes
    private double _delta;         // resolved declination applied to every plane
    private bool _plotReady;       // the three.js page reported {type:"ready"}
    private ImmutableArray<(Vec3 A, Vec3 B)> _caveLegs = ImmutableArray<(Vec3, Vec3)>.Empty;
    private Avalonia.Threading.DispatcherTimer? _persistTimer;

    /// <summary>Per-grid column visibility (header → shown). Persisted; applied by the view on attach.</summary>
    public Dictionary<string, bool> MeasurementColumns { get; private set; } = new();
    public Dictionary<string, bool> PlaneColumns { get; private set; } = new();

    public StructuralGeologyViewModel() { }   // design-time

    public StructuralGeologyViewModel(
        IDocumentService documents,
        IWorkspaceSession? session = null,
        IStructuralPlotAssetHost? plotHost = null,
        IAppSettingsService? settings = null,
        ILanguageService? language = null)
    {
        _documents = documents;
        _session = session;
        _plotHost = plotHost;
        _settings = settings;
        if (_documents is not null) _documents.DocumentChanged += (_, _) => { if (_activated) Rerun(); };
        // Re-run the analysis when the UI language changes so the status line and the localized row
        // labels (Kind, declination note, invalid-plane reason) re-render in the new language.
        if (language is not null) language.LanguageChanged += (_, _) => { if (_activated) Rerun(); };
        if (_settings is not null)
        {
            _persistTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _persistTimer.Tick += (_, _) => { _persistTimer!.Stop(); WriteSettings(); };
        }
        LoadSettings();
    }

    /// <summary>Raised to navigate the editor to a measurement's source span (double-click).</summary>
    public event EventHandler<SourceSpan>? NavigateRequested;

    /// <summary>Raised with a JS snippet the plot View should run via InvokeScript (C#→JS).</summary>
    public event EventHandler<string>? PlotScriptRequested;

    // ---- wizard state ----------------------------------------------------------------------------

    [ObservableProperty] private int _selectedTab;

    [ObservableProperty] private bool _projectScope = true;     // default whole project (#8); false = active file
    [ObservableProperty] private bool _groupByStation = true;   // #10: group the measurements grid by station
    [ObservableProperty] private bool _showFullStationName;     // full survey.station vs. the station only

    // detection options (bound in the Detect tab)
    [ObservableProperty] private bool _useNameKeyword = true;
    [ObservableProperty] private string _nameKeywords = "geo";
    [ObservableProperty] private bool _matchComment;
    [ObservableProperty] private string _commentMarkers = "plane, geo";
    [ObservableProperty] private bool _matchStationFlag;
    [ObservableProperty] private string _stationFlags = "";
    [ObservableProperty] private GroupingMode _grouping = GroupingMode.ByFromStation;
    [ObservableProperty] private SplayPolicy _splays = SplayPolicy.Exclude;
    [ObservableProperty] private bool _includeOriginPoint;

    // declination (Phase 3a: None / Manual; survey-declared + WMM-auto wired in 3b)
    [ObservableProperty] private DeclinationSource _declinationSource = DeclinationSource.None;
    [ObservableProperty] private double _declinationDegrees;

    // 3D plot disc-size multiplier (planes are tiny vs. the cave; let the user inflate them).
    [ObservableProperty] private double _discScale = 1;
    public double[] DiscScales { get; } = { 1, 5, 10, 20, 50 };
    [ObservableProperty] private bool _whiteBackground;   // #4: white vs. dark plot background

    // results
    public ObservableCollection<StructuralMeasurementRow> Measurements { get; } = new();
    public ObservableCollection<StructuralPlaneRow> Planes { get; } = new();
    [ObservableProperty] private StructuralPlaneRow? _selectedPlane;
    [ObservableProperty] private string _status = "Open a survey and press Detect.";

    // combo item sources
    public Array GroupingModes { get; } = Enum.GetValues(typeof(GroupingMode));
    public Array SplayPolicies { get; } = Enum.GetValues(typeof(SplayPolicy));
    public DeclinationSource[] DeclinationSources { get; } =
        { DeclinationSource.None, DeclinationSource.Manual, DeclinationSource.SurveyDeclared, DeclinationSource.WmmAuto };

    public bool DeclinationIsManual => DeclinationSource == DeclinationSource.Manual;

    // ---- option-change → re-run (immediate effect) -----------------------------------------------

    partial void OnProjectScopeChanged(bool value) => RerunAndPersist();
    partial void OnUseNameKeywordChanged(bool value) => RerunAndPersist();
    partial void OnNameKeywordsChanged(string value) => RerunAndPersist();
    partial void OnMatchCommentChanged(bool value) => RerunAndPersist();
    partial void OnCommentMarkersChanged(string value) => RerunAndPersist();
    partial void OnMatchStationFlagChanged(bool value) => RerunAndPersist();
    partial void OnStationFlagsChanged(string value) => RerunAndPersist();
    partial void OnGroupingChanged(GroupingMode value) => RerunAndPersist();
    partial void OnSplaysChanged(SplayPolicy value) => RerunAndPersist();
    partial void OnIncludeOriginPointChanged(bool value) => RerunAndPersist();
    partial void OnDeclinationSourceChanged(DeclinationSource value)
    {
        OnPropertyChanged(nameof(DeclinationIsManual));
        RerunAndPersist();
    }
    partial void OnDeclinationDegreesChanged(double value) => RerunAndPersist();

    private void RerunAndPersist() { Rerun(); Persist(); }

    /// <summary>Called by the view when the panel is first shown — kicks off the initial analysis.</summary>
    public void OnPanelActivated()
    {
        _activated = true;
        if (Measurements.Count == 0 && Planes.Count == 0) Rerun();
    }

    [RelayCommand] private void Refresh() => Rerun();

    [RelayCommand]
    private void NavigateToSelectedPlane()
    {
        if (SelectedPlane is { } p) NavigateRequested?.Invoke(this, p.Span);
    }

    public void Navigate(SourceSpan span)
    {
        if (!span.IsEmpty) NavigateRequested?.Invoke(this, span);
    }

    // ---- wizard tab navigation -------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanGoPrev))] private void GoPrev() => SelectedTab = Math.Max(0, SelectedTab - 1);
    [RelayCommand(CanExecute = nameof(CanGoNext))] private void GoNext() => SelectedTab = Math.Min(TabCount - 1, SelectedTab + 1);

    /// <summary>Public so the view can hide (not just disable) the buttons on the first/last tab (#6).</summary>
    public bool CanGoPrev => SelectedTab > 0;
    public bool CanGoNext => SelectedTab < TabCount - 1;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        GoPrevCommand.NotifyCanExecuteChanged();
        GoNextCommand.NotifyCanExecuteChanged();
        Persist();
    }

    partial void OnDiscScaleChanged(double value) { PushPlot(); Persist(); }
    partial void OnWhiteBackgroundChanged(bool value) { PushBackground(); Persist(); }

    /// <summary>Raised when group-by-station toggles so the view can rebuild the grid's grouped view.</summary>
    public event EventHandler? GroupingChanged;
    partial void OnGroupByStationChanged(bool value) { GroupingChanged?.Invoke(this, EventArgs.Empty); Persist(); }

    // Toggling full/short station names just re-reads the From/To columns (no re-fit); like the .th editor.
    partial void OnShowFullStationNameChanged(bool value)
    {
        foreach (var r in Measurements) r.RefreshStationNames();
        Persist();
    }

    // ---- bulk selection (check all / none / invert, and per-station toggles) ----------------------

    [RelayCommand] private void CheckAll() => SetAll(_ => true);
    [RelayCommand] private void CheckNone() => SetAll(_ => false);
    [RelayCommand] private void InvertSelection() => SetAll(r => !r.Include);

    private void SetAll(Func<StructuralMeasurementRow, bool> select)
    {
        _bulk = true;
        foreach (var r in Measurements) if (!r.IsOrigin) r.Include = select(r);
        _bulk = false;
        RecomputeAll();
    }

    // ---- plane visibility in the 3D plot (Resulted-planes grid checkboxes) ------------------------

    [RelayCommand] private void ShowAllPlanes() => SetAllPlanesVisible(_ => true);
    [RelayCommand] private void HideAllPlanes() => SetAllPlanesVisible(_ => false);
    [RelayCommand] private void InvertPlaneVisibility() => SetAllPlanesVisible(p => !p.Visible);

    private void SetAllPlanesVisible(Func<StructuralPlaneRow, bool> select)
    {
        _bulk = true;
        foreach (var p in Planes) p.Visible = select(p);
        _bulk = false;
        PushPlot();
    }

    /// <summary>A single plane's "Visible" checkbox toggled — re-push the plot (unless in a bulk op).</summary>
    public void OnPlaneVisibilityChanged()
    {
        if (_bulk) return;
        PushPlot();
    }

    /// <summary>
    /// Check/uncheck every real measurement of one station (its plane batch) in one go. The synthetic
    /// origin point is left alone — it stays governed by the Detect-tab "include origin" option, so the
    /// master toggle never silently pulls the origin into the fit.
    /// </summary>
    public void SetAllInPlane(StructuralPlaneRow plane, bool include)
    {
        _bulk = true;
        foreach (var r in plane.Rows) if (!r.IsOrigin) r.Include = include;
        _bulk = false;
        RecomputePlane(plane);
        PushPlot();
    }

    /// <summary>Recompute one batch's plane after the user toggled a single measurement's inclusion.</summary>
    public void RecomputeBatch(StructuralPlaneRow row)
    {
        if (_bulk) return;   // a bulk op recomputes once at the end
        RecomputePlane(row);
        PushPlot();
    }

    private void RecomputePlane(StructuralPlaneRow row)
    {
        var included = row.Rows.Where(r => r.Include).Select(r => r.Measurement).ToList();
        row.UpdatePlane(StructuralAnalysis.Recompute(row.Batch, included, _delta));
        row.RefreshIncludeAll();
    }

    private void RecomputeAll()
    {
        foreach (var p in Planes) RecomputePlane(p);
        PushPlot();
    }

    // ---- analysis --------------------------------------------------------------------------------

    private void Rerun()
    {
        if (_suspend || !_activated) return;

        var model = BuildScopedModel();
        Measurements.Clear();
        Planes.Clear();
        SelectedPlane = null;

        if (model is null || model.Shots.IsDefaultOrEmpty)
        {
            _delta = 0;
            Status = ThIDE.Resources.Tr.Get("Struct_StatusNoData");
            return;
        }

        var options = new StructuralOptions
        {
            Detection = BuildDetectionOptions(),
            Declination = new DeclinationOptions { Source = DeclinationSource, ManualDegrees = DeclinationDegrees },
            DeclinationInputs = ComputeDeclinationInputs(model),
        };

        var result = StructuralAnalysis.Analyze(model, options);
        _delta = result.Declination.Delta;
        _caveLegs = result.CaveLegs;

        for (int i = 0; i < result.Batches.Length; i++)
        {
            var planeRow = new StructuralPlaneRow(this, result.Batches[i], result.Planes[i]);
            foreach (var m in result.Batches[i].Measurements)
            {
                var row = new StructuralMeasurementRow(this, planeRow, m);
                planeRow.Rows.Add(row);
                Measurements.Add(row);
            }
            // Rows default to all-checked (except the opt-in origin), so the initial plane must be
            // refit from that state rather than the core's splay-aware DefaultIncluded() result.
            RecomputePlane(planeRow);
            Planes.Add(planeRow);
        }

        var deltaText = _delta.ToString("+0.0;-0.0", System.Globalization.CultureInfo.CurrentCulture);
        var declNote = result.Declination.Effective switch
        {
            DeclinationSource.Manual => string.Format(ThIDE.Resources.Tr.Get("Struct_DeclManual"), deltaText),
            DeclinationSource.SurveyDeclared => string.Format(ThIDE.Resources.Tr.Get("Struct_DeclSurvey"), deltaText),
            DeclinationSource.WmmAuto => string.Format(ThIDE.Resources.Tr.Get("Struct_DeclWmm"), deltaText),
            _ => result.Declination.Note is { } n ? $" · {n}" : "",
        };
        Status = string.Format(ThIDE.Resources.Tr.Get("Struct_StatusFmt"),
            Planes.Count, Measurements.Count(m => !m.IsOrigin), declNote);
        PushPlot();
    }

    // ---- 3D plot (three.js in a NativeWebView) ---------------------------------------------------

    public bool IsPlotAvailable => _plotHost?.IsAvailable ?? false;

    /// <summary>Called by the plot View when it creates the web control; returns the URL or null.</summary>
    public string? EnsurePlotStarted()
    {
        if (_plotHost is null || !_plotHost.IsAvailable) return null;
        return _plotHost.TryStart() ? _plotHost.ViewerUrl : null;
    }

    /// <summary>JS→C# bridge: the page is ready, or a plane disc was clicked.</summary>
    public void OnPlotMessage(string? json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "ready":
                    _plotReady = true;
                    PushBackground();
                    PushPlot();
                    break;
                case "pick":
                    var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is not null)
                        SelectedPlane = Planes.FirstOrDefault(p => p.Name == name) ?? SelectedPlane;
                    break;
                case "image":
                    var data = doc.RootElement.TryGetProperty("data", out var d) ? d.GetString() : null;
                    if (!string.IsNullOrEmpty(data)) PlotImageReady?.Invoke(this, data!);
                    break;
            }
        }
        catch { /* ignore malformed bridge messages */ }
    }

    /// <summary>Raised with a PNG data-URL the view should save (image export, #5).</summary>
    public event EventHandler<string>? PlotImageReady;

    [RelayCommand] private void ExportPlotImage() => PlotScriptRequested?.Invoke(this, "stExport()");

    private void PushBackground()
    {
        if (_plotReady) PlotScriptRequested?.Invoke(this, $"stSetBackground('{(WhiteBackground ? "#ffffff" : "#1e1e1e")}')");
    }

    private void PushPlot()
    {
        if (!_plotReady) return;
        PlotScriptRequested?.Invoke(this, $"stRender({BuildPlotJson()})");
    }

    private string BuildPlotJson()
    {
        var legs = new double[_caveLegs.Length * 6];
        for (int i = 0; i < _caveLegs.Length; i++)
        {
            var (a, b) = _caveLegs[i];
            int o = i * 6;
            legs[o] = a.E; legs[o + 1] = a.N; legs[o + 2] = a.Z;
            legs[o + 3] = b.E; legs[o + 4] = b.N; legs[o + 5] = b.Z;
        }

        var planes = new List<PlotPlaneDto>(Planes.Count);
        foreach (var row in Planes)
        {
            var plane = row.Plane;
            if (!plane.IsValid || !row.Visible) continue;   // hidden planes are dropped from the plot
            planes.Add(new PlotPlaneDto(
                row.Name,
                new[] { plane.Centroid.E, plane.Centroid.N, plane.Centroid.Z },
                new[] { plane.Normal.E, plane.Normal.N, plane.Normal.Z },
                DiscRadius(row, plane.Centroid) * DiscScale,
                true));
        }

        return JsonSerializer.Serialize(new PlotDto(legs, planes.ToArray()), PlotJsonOptions);
    }

    // Disc radius = farthest included point from the fitted centroid (in the fit frame).
    private static double DiscRadius(StructuralPlaneRow row, Vec3 centroid)
    {
        double max = 0;
        var inc = row.Rows.Where(r => r.Include).Select(r => r.Measurement).ToList();
        bool world = inc.Count > 0 && inc.All(m => m.World is not null);
        foreach (var m in inc)
        {
            var p = world ? m.World!.Value : m.Local;
            max = Math.Max(max, (p - centroid).Length);
        }
        return max > 1e-6 ? max : 1.0;
    }

    private static readonly JsonSerializerOptions PlotJsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record PlotPlaneDto(string Name, double[] Centroid, double[] Normal, double Radius, bool Valid);
    private sealed record PlotDto(double[] Legs, PlotPlaneDto[] Planes);

    // ---- tabular export (CSV / formatted copy) ---------------------------------------------------

    /// <summary>Column headers + string rows for the measurements grid (respects the name-length toggle).</summary>
    public (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) MeasurementsTable()
    {
        var headers = new[] { "Plane", "Kind", "From", "To", "Length", "Azimuth", "Clino", "Include", "Comment", "File", "Line" };
        var rows = Measurements.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Plane, r.Kind, r.From, r.To, r.Length, r.Compass, r.Clino,
            r.Include ? "yes" : "no", r.Comment, r.File, r.Line.ToString(),
        }).ToList();
        return (headers, rows);
    }

    /// <summary>Column headers + string rows for the resulted-planes grid.</summary>
    public (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) PlanesTable()
    {
        var headers = new[] { "Plane", "Dip °", "Strike °", "Dip dir °", "North ref", "Points", "RMS", "Visible", "File", "Line" };
        var rows = Planes.Select(p => (IReadOnlyList<string>)new[]
        {
            p.Name, p.Dip, p.Strike, p.DipDirection, p.Declination, p.Points, p.Quality,
            p.Visible ? "yes" : "no", p.File, p.Line.ToString(),
        }).ToList();
        return (headers, rows);
    }

    // ---- persistence (#2/#9: all tab settings + column visibility) -------------------------------

    private void LoadSettings()
    {
        var json = _settings?.Current.StructuralGeologySettings;
        if (string.IsNullOrEmpty(json)) { _loaded = true; return; }
        try
        {
            if (JsonSerializer.Deserialize<PanelState>(json) is { } s)
            {
                _suspend = true;
                ProjectScope = s.ProjectScope;
                GroupByStation = s.GroupByStation;
                ShowFullStationName = s.ShowFullStationName;
                UseNameKeyword = s.UseNameKeyword;
                NameKeywords = s.NameKeywords ?? NameKeywords;
                MatchComment = s.MatchComment;
                CommentMarkers = s.CommentMarkers ?? CommentMarkers;
                MatchStationFlag = s.MatchStationFlag;
                StationFlags = s.StationFlags ?? StationFlags;
                Grouping = s.Grouping;
                Splays = s.Splays;
                IncludeOriginPoint = s.IncludeOriginPoint;
                DeclinationSource = s.DeclinationSource;
                DeclinationDegrees = s.DeclinationDegrees;
                DiscScale = s.DiscScale <= 0 ? 1 : s.DiscScale;
                WhiteBackground = s.WhiteBackground;
                SelectedTab = Math.Clamp(s.SelectedTab, 0, TabCount - 1);
                if (s.MeasColumns is { } mc) MeasurementColumns = mc;
                if (s.PlaneColumns is { } pc) PlaneColumns = pc;
                _suspend = false;
            }
        }
        catch { _suspend = false; }
        _loaded = true;
    }

    /// <summary>Debounced persist; the view also calls this after toggling a column's visibility.</summary>
    public void Persist()
    {
        if (!_loaded || _settings is null) return;
        _persistTimer?.Stop();
        _persistTimer?.Start();
    }

    private void WriteSettings()
    {
        if (_settings is null) return;
        var state = new PanelState(
            ProjectScope, GroupByStation, UseNameKeyword, NameKeywords, MatchComment, CommentMarkers,
            MatchStationFlag, StationFlags, Grouping, Splays, IncludeOriginPoint, DeclinationSource,
            DeclinationDegrees, DiscScale, WhiteBackground, SelectedTab, MeasurementColumns, PlaneColumns,
            ShowFullStationName);
        try { _settings.Save(_settings.Current with { StructuralGeologySettings = JsonSerializer.Serialize(state) }); }
        catch { /* best-effort persistence */ }
    }

    private sealed record PanelState(
        bool ProjectScope, bool GroupByStation, bool UseNameKeyword, string NameKeywords, bool MatchComment,
        string CommentMarkers, bool MatchStationFlag, string StationFlags, GroupingMode Grouping, SplayPolicy Splays,
        bool IncludeOriginPoint, DeclinationSource DeclinationSource, double DeclinationDegrees, double DiscScale,
        bool WhiteBackground, int SelectedTab, Dictionary<string, bool> MeasColumns, Dictionary<string, bool> PlaneColumns,
        bool ShowFullStationName = false);

    private DetectionOptions BuildDetectionOptions() => new()
    {
        NameKeywords = UseNameKeyword ? SplitTokens(NameKeywords) : ImmutableArray<string>.Empty,
        MatchComment = MatchComment,
        CommentMarkers = SplitTokens(CommentMarkers),
        MatchStationFlag = MatchStationFlag,
        StationFlags = SplitTokens(StationFlags),
        Splays = Splays,
        Grouping = Grouping,
        IncludeOriginPoint = IncludeOriginPoint,
    };

    private static ImmutableArray<string> SplitTokens(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? ImmutableArray<string>.Empty
            : text.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToImmutableArray();

    /// <summary>The SemanticModel to analyse: the active file, or a merged whole-project model.</summary>
    private SemanticModel? BuildScopedModel()
    {
        if (_documents is null) return null;
        if (!ProjectScope) return _documents.CurrentSemantics;

        var ws = _documents.Workspace;
        if (ws is null || ws.PerFile.Count == 0) return _documents.CurrentSemantics;

        var models = ws.PerFile.Values.ToList();
        var shots = models.SelectMany(m => m.Shots).ToImmutableArray();
        var stations = models
            .SelectMany(m => (IEnumerable<KeyValuePair<QualifiedName, StationSymbol>>)m.Stations)
            .GroupBy(kv => kv.Key)
            .ToFrozenDictionary(g => g.Key, g => g.First().Value);
        var equates = LivePreviewViewModel.BuildEquateGraph(models);
        return new SemanticModel(
            stations,
            FrozenDictionary<QualifiedName, SurveySymbol>.Empty,
            shots,
            equates,
            ImmutableArray<Diagnostic>.Empty)
        {
            Declination = models.Select(m => m.Declination).FirstOrDefault(d => d is not null),
        };
    }

    // ---- declination inputs (Phase 3b: WMM-auto from the fix point + survey date) ----------------

    private DeclinationInputs ComputeDeclinationInputs(SemanticModel model)
    {
        if (DeclinationSource == DeclinationSource.SurveyDeclared)
            return new DeclinationInputs(SurveyDeclaredDegrees: model.Declination);

        if (DeclinationSource != DeclinationSource.WmmAuto) return default;

        StationSymbol? fix = null;
        foreach (var s in model.Stations.Values)
            if (s.FixX is not null && s.FixY is not null) { fix = s; break; }
        if (fix is null) return new DeclinationInputs(WmmNote: "WMM: no fix point in scope");

        if (!CoordinateTransform.TryToWgs84(fix.Cs, fix.FixX!.Value, fix.FixY!.Value, out var ll))
            return new DeclinationInputs(WmmNote: $"WMM: can't convert fix cs '{fix.Cs ?? "?"}' to lat/lon");

        var geo = GeoMagneticModelLoader.TryLoadDefault();
        if (geo is null)
            return new DeclinationInputs(WmmNote: "WMM: no WMM.COF model (add one in %AppData%/ThIDE)");

        double year = SurveyYear(model);
        double d = geo.Declination(ll.Lat, ll.Lon, 0, year);
        return new DeclinationInputs(
            WmmAutoDegrees: d,
            WmmNote: $"WMM {geo.Name} · {ll.Lat:0.0},{ll.Lon:0.0} · {year:0.0}");
    }

    private static double SurveyYear(SemanticModel model)
    {
        foreach (var s in model.Surveys.Values)
            foreach (var d in s.Dates)
                if (TryParseDecimalYear(d, out var y)) return y;
        var now = System.DateTime.UtcNow;
        return now.Year + (now.DayOfYear - 1) / 365.0;
    }

    private static bool TryParseDecimalYear(string? raw, out double year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var first = raw.Split(new[] { ' ', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (first.Length == 0) return false;
        var parts = first[0].Split('.');
        if (!int.TryParse(parts[0], out var yr) || yr < 1000 || yr > 3000) return false;
        double frac = 0;
        if (parts.Length > 1 && int.TryParse(parts[1], out var mo) && mo is >= 1 and <= 12) frac = (mo - 1) / 12.0;
        year = yr + frac;
        return true;
    }
}

/// <summary>A row in the raw-measurements grid; its <see cref="Include"/> drives the live re-fit.</summary>
public sealed partial class StructuralMeasurementRow : ObservableObject
{
    private readonly StructuralGeologyViewModel _owner;
    public StructuralPlaneRow PlaneRow { get; }
    public StructuralMeasurement Measurement { get; }

    [ObservableProperty] private bool _include;

    public StructuralMeasurementRow(StructuralGeologyViewModel owner, StructuralPlaneRow planeRow, StructuralMeasurement m)
    {
        _owner = owner;
        PlaneRow = planeRow;
        Measurement = m;
        // Default: every real measurement (leg/splay) checked; the synthetic origin point stays opt-in.
        _include = m.IsOrigin ? m.IncludedByDefault : true;
    }

    partial void OnIncludeChanged(bool value) => _owner.RecomputeBatch(PlaneRow);

    public string Plane => PlaneRow.Name;
    public string From => Measurement.IsOrigin ? "⟨origin⟩" : StationName(Measurement.From);
    public string To => Measurement.IsOrigin ? "" : StationName(Measurement.To);
    // Full "survey.station" vs. the bare station name, matching the .th editor's measurements toggle.
    private string StationName(QualifiedName n) => _owner.ShowFullStationName ? n.ToString() : n.Last;
    public string Length => Measurement.Length?.ToString("0.##") ?? "";
    public string Compass => Measurement.Compass?.ToString("0.#") ?? "";
    public string Clino => Measurement.Clino?.ToString("0.#") ?? "";
    public string Kind => Measurement.IsOrigin ? ThIDE.Resources.Tr.Get("Struct_KindOrigin")
        : Measurement.IsSplay ? ThIDE.Resources.Tr.Get("Struct_KindSplay")
        : ThIDE.Resources.Tr.Get("Struct_KindLeg");
    public bool IsOrigin => Measurement.IsOrigin;
    public string Comment => Measurement.Comment ?? "";
    /// <summary>Comment clipped to a compact grid width (full text is shown in the cell tooltip).</summary>
    public string CommentShort => Comment.Length > 25 ? Comment[..25] + "…" : Comment;
    public string File => string.IsNullOrEmpty(Measurement.SourceFile) ? "" : Path.GetFileName(Measurement.SourceFile);
    public int Line => Measurement.Line;
    public SourceSpan Span => Measurement.Span;

    /// <summary>Re-read the From/To columns after the full/short station-name toggle flips.</summary>
    public void RefreshStationNames() { OnPropertyChanged(nameof(From)); OnPropertyChanged(nameof(To)); }

    public void NavigateToSource() => _owner.Navigate(Span);
}

/// <summary>A row in the resulted-planes grid; updated in place when its batch is re-fitted.</summary>
public sealed partial class StructuralPlaneRow : ObservableObject
{
    private readonly StructuralGeologyViewModel _owner;

    public StructuralBatch Batch { get; }
    public List<StructuralMeasurementRow> Rows { get; } = new();

    [ObservableProperty] private FittedPlane _plane;

    /// <summary>Whether this plane's disc is drawn in the 3D plot (checkbox in the resulted-planes grid).</summary>
    [ObservableProperty] private bool _visible = true;

    public StructuralPlaneRow(StructuralGeologyViewModel owner, StructuralBatch batch, FittedPlane plane)
    {
        _owner = owner;
        Batch = batch;
        _plane = plane;
    }

    partial void OnVisibleChanged(bool value) => _owner.OnPlaneVisibilityChanged();

    public void UpdatePlane(FittedPlane plane) => Plane = plane;

    // Refresh every derived display column when the fit changes.
    partial void OnPlaneChanged(FittedPlane value) => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Master toggle for this station's measurements (a two-state "select all": checked only when every
    /// real measurement is on; a partial station reads as unchecked). The synthetic origin point is
    /// excluded from the tally, so a freshly-detected station — where every leg/splay is on by default
    /// and only the opt-in origin is off — still reads as checked. Setting it checks/unchecks the whole
    /// station in one recompute. Bound by the per-station group header checkbox.
    /// </summary>
    public bool IncludeAllChecked
    {
        get
        {
            bool any = false;
            foreach (var r in Rows)
            {
                if (r.IsOrigin) continue;
                if (!r.Include) return false;
                any = true;
            }
            return any;
        }
        set => _owner.SetAllInPlane(this, value);
    }

    /// <summary>Raised by the owner after this station's inclusion changed so the toggle re-reads.</summary>
    public void RefreshIncludeAll() => OnPropertyChanged(nameof(IncludeAllChecked));

    // Group label when the measurements grid groups by PlaneRow (#10).
    public override string ToString() => Name;

    public string Name => Batch.Name;
    public string Dip => Plane.IsValid ? Plane.Dip.ToString("0.0") : "—";
    public string Strike => Plane.IsValid ? Plane.Strike.ToString("0.0") : "—";
    public string DipDirection => Plane.IsValid ? Plane.DipDirection.ToString("0.0") : "—";
    public string Declination => !Plane.IsValid ? "—"
        : Plane.DeclinationApplied != 0 ? $"{Plane.DeclinationApplied:+0.0;-0.0}° (true N)" : "magnetic";
    public string Points => Plane.PointCount.ToString();
    public string Quality => Plane.IsValid ? Plane.RmsResidual.ToString("0.###") + " m" : (Plane.ErrorReason ?? ThIDE.Resources.Tr.Get("Struct_Invalid"));
    public bool IsValid => Plane.IsValid;
    public string File => string.IsNullOrEmpty(Batch.SourceFile) ? "" : Path.GetFileName(Batch.SourceFile);
    public int Line => Rows.FirstOrDefault(r => !r.IsOrigin)?.Line ?? 0;
    public SourceSpan Span => Rows.FirstOrDefault(r => !r.IsOrigin)?.Span ?? SourceSpan.None;
}
