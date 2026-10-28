using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Import;
using Therion.Workspace.Linking;
using Therion.Workspace.Templates;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels.Pickers;

namespace ThIDE.ViewModels;

/// <summary>What Tools ▸ New trip… needs from the app, captured when it opens.</summary>
public sealed record NewTripEnvironment(
    WorkspaceSemanticModel Workspace,
    string? WorkspaceRoot,
    Func<string, string?> ReadText,
    Func<string, bool> Exists,
    IWorkspaceEditService? Editor,
    Func<string, int, Task> Open,
    DateTime Today);

public sealed partial class TripTeamRow(string name, bool isChecked) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool _isChecked = isChecked;
}

/// <summary>
/// Tools ▸ New trip…: a new survey file for a trip, prefilled from the latest trip, tied to a
/// project station with an <c>input</c> and an <c>equate</c> written where the project keeps them.
/// </summary>
public sealed partial class NewTripViewModel : ObservableObject
{
    private readonly NewTripEnvironment _env;
    private LinkContext? _ctx;
    private FileScopes? _scopes;
    private bool _nameEdited, _folderEdited, _settingName, _settingFolder;
    private int _quiet;

    public NewTripViewModel(NewTripEnvironment env)
    {
        _env = env;
        _date = env.Today.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        _surveyName = DefaultName(_date);
        Surveys.SelectionChanged += (_, _) => NarrowStations();
        Stations.SelectionChanged += (_, _) => RefreshDestinations();
    }

    public SurveyPickerViewModel Surveys { get; } = new("TripSurvey");
    public StationPickerViewModel Stations { get; } = new("TripStation");
    public ObservableCollection<TripTeamRow> Team { get; } = new();
    public ObservableCollection<DestinationRow> Destinations { get; } = new();

    [ObservableProperty] private string _date;
    [ObservableProperty] private string _extraTeam = string.Empty;
    [ObservableProperty] private string _units = "units length meters";
    [ObservableProperty] private string _calibrate = string.Empty;
    [ObservableProperty] private string _instrument = string.Empty;
    [ObservableProperty] private string _data = "data normal from to length compass clino";
    [ObservableProperty] private string _surveyName;
    [ObservableProperty] private string _firstStation = "0";
    [ObservableProperty] private bool _nest;
    [ObservableProperty] private DestinationRow? _selectedDestination;
    [ObservableProperty] private string _folder = string.Empty;
    [ObservableProperty] private string _generated = string.Empty;
    [ObservableProperty] private IReadOnlyList<WarningRow> _warnings = Array.Empty<WarningRow>();
    [ObservableProperty] private bool _canCreate;

    public string FilePath => Path.Combine(Folder, SurveyName + ".th");

    private static string DefaultName(string date) => "trip_" + date.Replace('.', '_');

    /// <summary>Builds the scopes and lists, and reads the defaults from the latest trip.</summary>
    public async Task LoadAsync()
    {
        _quiet++;
        try
        {
            TherionFile? Ast(string p) => p.EndsWith(".th", StringComparison.OrdinalIgnoreCase) && _env.ReadText(p) is { } t ? new ThParser().Parse(p, t).Value : null;
            var cache = new Dictionary<string, TherionFile?>(FilePaths.Comparer);
            TherionFile? Cached(string p) { lock (cache) { if (cache.TryGetValue(p, out var a)) return a; } var r = Ast(p); lock (cache) cache[p] = r; return r; }
            TripDefaults defaults = TripDefaults.Empty;
            await Task.Run(() =>
            {
                _scopes = SurveyScopeResolver.Build(_env.Workspace, Cached);
                _ctx = new LinkContext(_env.Workspace, _scopes, _env.ReadText, Cached, _env.Exists);
                defaults = TripDefaults.From(_env.Workspace, _env.ReadText, Cached);
            });
            Surveys.SetSurveys(LinkCatalog.Surveys(_env.Workspace, _scopes!));
            Stations.SetStations(LinkCatalog.Stations(_env.Workspace, _scopes!), showFile: true);

            if (defaults.Units is { } u) Units = u;
            Calibrate = defaults.Calibrate ?? "";
            Instrument = defaults.Instrument ?? "";
            if (defaults.Data is { } d) Data = d;
            var recent = defaults.Team ?? "";
            foreach (var name in defaults.TeamMembers)
                Team.Add(new TripTeamRow(name, recent.Contains('"' + name + '"', StringComparison.Ordinal)));
            foreach (var row in Team) row.PropertyChanged += (_, _) => Refresh();
        }
        finally { _quiet--; }
        Refresh();
    }

    private void NarrowStations()
    {
        if (_scopes is null) return;
        Stations.SetStations(LinkCatalog.Stations(_env.Workspace, _scopes, Surveys.SelectedSurvey?.Effective), showFile: true);
    }

    private void RefreshDestinations()
    {
        if (_ctx is null) return;
        var previous = SelectedDestination?.Candidate;
        _quiet++;
        try
        {
            Destinations.Clear();
            if (Stations.SelectedStation is { } tie)
                foreach (var c in SurveyLinkPlanner.Candidates(_ctx, SurveyLinkPlanner.DefaultScope(tie.Effective, Nest)))
                    Destinations.Add(new DestinationRow(c, Label(c)));
            SelectedDestination = Destinations.FirstOrDefault(d => previous is not null && d.Candidate.FilePath == previous.FilePath
                                      && d.Candidate.LocalScope.SequenceEqual(previous.LocalScope))
                                  ?? Destinations.FirstOrDefault();
        }
        finally { _quiet--; }
        Refresh();
    }

    private string Label(DestinationCandidate c)
    {
        var scope = c.Scope.IsEmpty ? Tr.Get("LinkSurvey_RootScope") : string.Join('.', c.Scope);
        var file = _env.WorkspaceRoot is { } root ? Path.GetRelativePath(root, c.FilePath).Replace('\\', '/') : Path.GetFileName(c.FilePath);
        return $"{file} — {scope} ({Tr.Get("LinkSurvey_Reason" + c.Reason)})";
    }

    // A different scope: offer its best destination rather than keeping the old choice.
    partial void OnNestChanged(bool value)
    {
        _quiet++;
        SelectedDestination = null;
        _quiet--;
        RefreshDestinations();
    }
    partial void OnSelectedDestinationChanged(DestinationRow? value)
    {
        if (!_folderEdited && value is not null && _ctx is not null)
        {
            _settingFolder = true;
            Folder = SurveyLinkPlanner.DefaultTargetDirectory(_ctx, value.Candidate.Destination);
            _settingFolder = false;
        }
        Refresh();
    }
    partial void OnDateChanged(string value)
    {
        if (!_nameEdited)
        {
            _settingName = true;
            SurveyName = DefaultName(value.Trim());
            _settingName = false;
        }
        Refresh();
    }
    partial void OnSurveyNameChanged(string value) { if (!_settingName) _nameEdited = true; Refresh(); }
    partial void OnFolderChanged(string value) { if (!_settingFolder) _folderEdited = true; Refresh(); }
    partial void OnExtraTeamChanged(string value) => Refresh();
    partial void OnUnitsChanged(string value) => Refresh();
    partial void OnCalibrateChanged(string value) => Refresh();
    partial void OnInstrumentChanged(string value) => Refresh();
    partial void OnDataChanged(string value) => Refresh();
    partial void OnFirstStationChanged(string value) => Refresh();

    /// <summary>The new file's text and where its caret goes (the empty line for the first leg).</summary>
    public (string Text, int Caret) Body()
    {
        var sb = new StringBuilder();
        sb.Append("encoding utf-8\n");
        sb.Append("survey ").Append(SurveyName.Trim()).Append(" -title \"").Append(SurveyName.Trim()).Append("\"\n");
        sb.Append("  centreline\n");
        sb.Append("    date ").Append(Date.Trim()).Append('\n');
        foreach (var name in TeamNames()) sb.Append("    team \"").Append(name).Append("\"\n");
        foreach (var line in new[] { Units, Calibrate, Instrument, Data })
            foreach (var l in line.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0))
                sb.Append("    ").Append(l.Trim()).Append('\n');
        sb.Append("    ");
        int caret = sb.Length;
        sb.Append("\n  endcentreline\nendsurvey\n");
        return (sb.ToString(), caret);
    }

    private IEnumerable<string> TeamNames() =>
        Team.Where(t => t.IsChecked).Select(t => t.Name)
            .Concat(ExtraTeam.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            .Select(n => n.Replace("\"", "")).Distinct(StringComparer.OrdinalIgnoreCase);

    public LinkPlan? Plan { get; private set; }

    private void Refresh()
    {
        if (_quiet > 0) return;
        Plan = null;
        CanCreate = false;
        var warnings = new List<WarningRow>();
        // The survey name is also the file name: no path separators.
        if (!ScaffoldValueRules.IsSurveyName(SurveyName.Trim()) || SurveyName.Contains('/')
            || SurveyName.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            warnings.Add(new(Tr.Get("NewTrip_BadName"), true));
        if (!ScaffoldValueRules.IsStationRef(FirstStation.Trim()) || FirstStation.Contains('@')) warnings.Add(new(Tr.Get("NewTrip_BadStation"), true));
        if (!TripDefaults.IsDate(Date))
            warnings.Add(new(Tr.Get("NewTrip_BadDate"), true));
        if (Stations.SelectedStation is not { } tie || SelectedDestination is not { } dest || _ctx is null)
        {
            if (Stations.SelectedStation is null) warnings.Add(new(Tr.Get("NewTrip_PickTieIn"), true));
            Warnings = warnings;
            Generated = Body().Text;
            return;
        }
        if (_env.Exists(FilePath)) warnings.Add(new(string.Format(Tr.Get("NewFile_Exists")), true));

        var (text, _) = Body();
        Generated = text;
        if (warnings.Any(w => w.Blocks)) { Warnings = warnings; return; }

        var ast = new ThParser().Parse(FilePath, text).Value!;
        var source = new SourceSurvey(FilePath, ast, new SemanticBinder().Bind(ast));
        var request = new LinkRequest(source,
            [new LinkMember(LinkSide.Source, FilePath, QualifiedName.Of(SurveyName.Trim(), FirstStation.Trim())),
             new LinkMember(LinkSide.Workspace, tie.FilePath, tie.Local)],
            dest.Candidate.Destination, LinkCopy.InPlace, new LinkOptions(Comment: Tr.Get("NewTrip_CheckTieIn")));
        Plan = SurveyLinkPlanner.Plan(_ctx, request);
        warnings.AddRange(Plan.Warnings.Where(w => w.Code != LinkWarningCode.SketchNotCopied)
            .Select(w => new WarningRow(string.Format(Tr.Get("LinkSurvey_Warn" + w.Code), w.Subject ?? ""), w.Blocks)));
        Warnings = warnings;
        CanCreate = !warnings.Any(w => w.Blocks) && _env.Editor is not null && Plan.Batch.Edits.Count > 0;
    }

    /// <summary>Writes the file and the lines that tie it in, then opens it. Null on success.</summary>
    public async Task<string?> CreateAsync()
    {
        if (Plan is not { } plan || _env.Editor is null) return Tr.Get("NewFile_CannotCreate");
        var (text, caret) = Body();
        var batch = new EditBatch(plan.Batch.Edits, [new FileCreate(FilePath, text)], [], RequiresWorkspaceReload: true);
        var outcome = await _env.Editor.ApplyAsync(batch);
        if (!outcome.Success)
            return outcome.Describe();
        await _env.Open(FilePath, caret);
        return null;
    }
}
