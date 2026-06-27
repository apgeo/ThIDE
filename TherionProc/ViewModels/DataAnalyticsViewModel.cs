// DATA-01/02/05/06/08 — survey-domain analytics view-model. Reads the workspace semantic model
// (via IDocumentService) and the project-analytics feature toggle (via IAppSettingsService),
// projecting DataAnalytics results into bind-friendly rows. Heavy work is gated by the setting so
// big projects can turn it off (DATA performance switch).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Semantics;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public sealed record StatLine(string Label, string Value);
/// <summary>One chart bar; <see cref="BarWidth"/> is pre-scaled to pixels for the lightweight bar view.</summary>
public sealed record ChartBar(string Label, string Value, double Fraction)
{
    public double BarWidth => Math.Max(1, Fraction * 240);
}
public sealed record TeamRow(string Name, int Surveys, string Length);
public sealed record ExpeditionRow(string Date, int Surveys, string Length, string Members);
public sealed record FixedRow(string Station, string Kind, string Coordinates, string Cs, string Location);
public sealed record QualityRow(string Metric, int Count);

public sealed partial class DataAnalyticsViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;
    private readonly IAppSettingsService? _settings;

    public ObservableCollection<StatLine> Statistics { get; } = new();          // DATA-01
    public ObservableCollection<ChartBar> LengthBySurvey { get; } = new();      // DATA-02
    public ObservableCollection<ChartBar> LengthByDate { get; } = new();        // DATA-02
    public ObservableCollection<TeamRow> Team { get; } = new();                 // DATA-05
    public ObservableCollection<ExpeditionRow> Expeditions { get; } = new();    // DATA-05
    public ObservableCollection<FixedRow> FixedPoints { get; } = new();         // DATA-06
    public ObservableCollection<QualityRow> Quality { get; } = new();           // DATA-08

    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _summary = "—";

    public DataAnalyticsViewModel() { } // design-time

    public DataAnalyticsViewModel(IDocumentService documents, IAppSettingsService settings)
    {
        _documents = documents;
        _settings = settings;
        _documents.DocumentChanged += (_, _) => ProjectFormat.OnUi(Rebuild);
        _settings.Changed += (_, _) => ProjectFormat.OnUi(Rebuild);
        Rebuild();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    [RelayCommand] private void CopyStatistics() =>
        ClipboardHelper.SetText(DataExport.ToMarkdown(new[] { "Metric", "Value" },
            Statistics.Select(s => (IReadOnlyList<string>)new[] { s.Label, s.Value })));

    [RelayCommand] private void CopyTeam() =>
        ClipboardHelper.SetText(DataExport.ToCsv(new[] { "Name", "Surveys", "Length" },
            Team.Select(t => (IReadOnlyList<string>)new[] { t.Name, t.Surveys.ToString(), t.Length })));

    [RelayCommand] private void CopyFixedPoints() =>
        ClipboardHelper.SetText(DataExport.ToCsv(new[] { "Station", "Kind", "Coordinates", "CRS", "Location" },
            FixedPoints.Select(f => (IReadOnlyList<string>)new[] { f.Station, f.Kind, f.Coordinates, f.Cs, f.Location })));

    private void Rebuild()
    {
        IsEnabled = _settings?.Current.EnableProjectAnalytics ?? true;
        Clear();
        if (!IsEnabled) { Summary = "Project analytics disabled in Preferences."; return; }

        var model = _documents?.Workspace;
        if (model is null || model.PerFile.Count == 0) { Summary = "No project open."; return; }

        BuildStatistics(model);
        BuildCharts(model);
        BuildTeam(model);
        BuildFixedPoints(model);
        BuildQuality(model);
        Summary = $"{Statistics.Count} metrics · {Team.Count} people · {FixedPoints.Count} fixed/entrance points";
    }

    private void Clear()
    {
        Statistics.Clear(); LengthBySurvey.Clear(); LengthByDate.Clear();
        Team.Clear(); Expeditions.Clear(); FixedPoints.Clear(); Quality.Clear();
    }

    private void BuildStatistics(WorkspaceSemanticModel model)
    {
        var t = DataAnalytics.ComputeDetailedTotals(model);
        Statistics.Add(new("Surveys", t.Surveys.ToString()));
        Statistics.Add(new("Stations", t.Stations.ToString()));
        Statistics.Add(new("Legs (shots)", t.Shots.ToString()));
        Statistics.Add(new("Splay shots", t.SplayShots.ToString()));
        Statistics.Add(new("Duplicate shots", t.DuplicateShots.ToString()));
        Statistics.Add(new("Surface shots", t.SurfaceShots.ToString()));
        Statistics.Add(new("Total length", ProjectFormat.Length(t.TotalLength)));
        Statistics.Add(new("  underground", ProjectFormat.Length(t.UndergroundLength)));
        Statistics.Add(new("  surface", ProjectFormat.Length(t.SurfaceLength)));
        Statistics.Add(new("  duplicate (excl.)", ProjectFormat.Length(t.DuplicateLength)));
        Statistics.Add(new("  splay (excl.)", ProjectFormat.Length(t.SplayLength)));
        Statistics.Add(new("Vertical range", ProjectFormat.Length(t.VerticalRange)));
        if (t.HighestStation is not null) Statistics.Add(new("  highest station", t.HighestStation));
        if (t.LowestStation is not null) Statistics.Add(new("  lowest station", t.LowestStation));
        Statistics.Add(new("Horizontal extent", $"{ProjectFormat.Length(t.EastWestExtent)} E–W · {ProjectFormat.Length(t.NorthSouthExtent)} N–S"));
        Statistics.Add(new("Entrances", t.Entrances.ToString()));
        Statistics.Add(new("Fixed points", t.FixedPoints.ToString()));
    }

    private void BuildCharts(WorkspaceSemanticModel model)
    {
        Fill(LengthBySurvey, DataAnalytics.LengthBySurvey(model).Take(30)
            .Select(b => (b.Key, b.Length)));
        Fill(LengthByDate, DataAnalytics.LengthByDate(model)
            .Select(b => (b.Key, b.Length)));

        static void Fill(ObservableCollection<ChartBar> target, IEnumerable<(string Key, double Length)> src)
        {
            var list = src.ToList();
            double max = list.Count == 0 ? 0 : list.Max(b => b.Length);
            foreach (var (key, len) in list)
                target.Add(new ChartBar(key, ProjectFormat.Length(len),
                    max > 0 ? Math.Clamp(len / max, 0, 1) : 0));
        }
    }

    private void BuildTeam(WorkspaceSemanticModel model)
    {
        foreach (var m in DataAnalytics.TeamMembers(model))
            Team.Add(new TeamRow(m.Name, m.Surveys, ProjectFormat.Length(m.Length)));
        foreach (var e in DataAnalytics.Expeditions(model))
            Expeditions.Add(new ExpeditionRow(e.Date, e.Surveys, ProjectFormat.Length(e.Length),
                string.Join(", ", e.Members)));
    }

    private void BuildFixedPoints(WorkspaceSemanticModel model)
    {
        foreach (var f in DataAnalytics.FixedPoints(model))
        {
            string coords = f.X is not null && f.Y is not null
                ? $"{Fmt(f.X)} {Fmt(f.Y)} {Fmt(f.Z)}"
                : "—";
            string kind = (f.IsFixed, f.IsEntrance) switch
            {
                (true, true) => "fixed entrance",
                (true, false) => "fixed",
                (false, true) => "entrance",
                _ => "",
            };
            FixedPoints.Add(new FixedRow(f.Station, kind, coords, f.Cs,
                $"{System.IO.Path.GetFileName(f.File)}:{f.Line}"));
        }
    }

    private void BuildQuality(WorkspaceSemanticModel model)
    {
        var q = DataAnalytics.DataQuality(model);
        Quality.Add(new("Total legs", q.TotalShots));
        Quality.Add(new("Zero-length legs", q.ZeroLength));
        Quality.Add(new("Legs missing length", q.MissingLength));
        Quality.Add(new("Legs missing compass", q.MissingCompass));
        Quality.Add(new("Legs missing clino", q.MissingClino));
        Quality.Add(new("Legs without backsight style", q.NoBacksight));
        Quality.Add(new("Legs without LRUD", q.NoLrud));
        Quality.Add(new("Steep legs (80–90°)", q.SteepLegs));
        Quality.Add(new("Splay shots", q.SplayShots));
        Quality.Add(new("Duplicate shots", q.DuplicateShots));
        Quality.Add(new("Undated surveys", q.UndatedSurveys));
        Quality.Add(new("Surveys without team", q.TeamlessSurveys));
    }

    private static string Fmt(double? v) =>
        v is { } d ? d.ToString("0.##", CultureInfo.InvariantCulture) : "—";
}
