// The branch table of a tour shot in the Storyboard tab. A tour shot ("branches": "auto") is made
// into its tour where the model is read; here the model is read on asking (List branches), the
// tour is worked out as the render will work it out, and its side passages are listed in the
// order they are wanted, with what the film does about each. The user visits one whatever the
// rules say, leaves one out, or moves it up or down; that is written into the shot's route
// (include, exclude, priority) and the tour is worked out again, the model kept from the first
// time.
//
// The VM holds no tour logic: StoryboardExpander and TourBuilder compute.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Blender;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.CaveGeometry;
using Therion.Cave3D.Parsing;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class BlenderAnimationViewModel
{
    /// <summary>The side passages of the selected tour shot, the one most wanted first.</summary>
    public ObservableCollection<TourBranchRowViewModel> TourBranches { get; } = [];

    /// <summary>What the tour left out, cut short or changed.</summary>
    public ObservableCollection<string> TourNotices { get; } = [];

    /// <summary>The tour in a line: its shots and its length.</summary>
    [ObservableProperty] private string? _tourSummary;

    /// <summary>True while the model is read and the tour worked out.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ListBranchesCommand))]
    private bool _isPlanningTour;

    /// <summary>True when the selected shot is a tour of the cave's branches.</summary>
    public bool IsTourShot => SelectedShot is { IsTour: true };

    public bool HasTourBranches => TourBranches.Count > 0;

    /// <summary>The tour being worked out, for a test to wait for; null when none is.</summary>
    internal Task? TourPlanning { get; private set; }

    // The model read for the table, kept while the same file is used.
    private (string Path, DateTime Written, GeometryResult Geometry, IReadOnlyList<SourceLead> Leads)? _tourModel;
    private StoryboardShotViewModel? _tourShot;
    private int _tourVersion;

    // The ids as the table ordered them: written into the route once a row was moved.
    private bool _branchOrderSaid;

    private bool CanListBranches => IsTourShot && !IsPlanningTour && !IsBusy && HasSource && _sources is not null;

    /// <summary>Reads the model and lists the tour's side passages.</summary>
    [RelayCommand(CanExecute = nameof(CanListBranches))]
    private async Task ListBranchesAsync()
    {
        if (SelectedShot is not { IsTour: true } row || _sources is null) return;
        LastError = null;
        IsPlanningTour = true;
        try
        {
            var source = await _sources.AcquireAsync(BuildRequest()).ConfigureAwait(true);
            string path = source.Model.Path;
            var written = File.GetLastWriteTimeUtc(path);
            if (_tourModel is not { } kept || kept.Path != path || kept.Written != written)
            {
                var geometry = await Task.Run(() => GeometryStage.Build(CaveModelReader.ReadFile(path))).ConfigureAwait(true);
                _tourModel = (path, written, geometry, source.Leads);
            }
            _tourShot = row;
            _branchOrderSaid = row.Camera?.Route?.Priority is { Count: > 0 };
            await PlanTourAsync(fresh: true).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException
                                       or NotSupportedException or ModelSourceNotFoundException or CaveFileFormatException)
        {
            LastError = ex.Message.Split(" (Parameter", 2)[0];
        }
        finally
        {
            IsPlanningTour = false;
        }
    }

    /// <summary>Works the tour out again for the rows shown: what the film does about each.</summary>
    private Task PlanTourAsync(bool fresh)
    {
        if (_tourModel is not { } model || _tourShot is not { } row || !Shots.Contains(row)) return Task.CompletedTask;
        int version = ++_tourVersion;
        TourSpec spec;
        try { spec = StoryboardExpander.TourSpecOf(CurrentStoryboard, Shots.IndexOf(row), FindPreset, FilmLanguage ?? InterfaceFilmLanguage); }
        catch (ArgumentException)
        {
            // The shot is no longer a tour.
            ClearBranchTable();
            return Task.CompletedTask;
        }
        // The presets as they are now: the tour is worked out on another thread.
        var presets = Presets.Select(p => p.Preset).ToList();
        RenderPreset? Find(string name) => presets.FirstOrDefault(p => p.Name == name);
        var work = Task.Run(() => TourBuilder.Build(model.Geometry, spec, model.Leads, Find));
        return TourPlanning = Show(work, version, fresh, spec);
    }

    private async Task Show(Task<Tour> work, int version, bool fresh, TourSpec spec)
    {
        Tour tour;
        try { tour = await work.ConfigureAwait(true); }
        catch (ArgumentException ex)
        {
            if (version == _tourVersion) LastError = ex.Message.Split(" (Parameter", 2)[0];
            return;
        }
        if (version != _tourVersion) return;

        var sides = tour.Rows.Where(r => r.Id != "trunk").ToList();
        if (fresh || TourBranches.Count != sides.Count)
        {
            TourBranches.Clear();
            foreach (var side in Ordered(sides, spec.Priority))
                TourBranches.Add(new TourBranchRowViewModel(side, VisitOf(side.Id, spec), BranchVisitChanged));
        }
        else
        {
            foreach (var shown in TourBranches)
                if (sides.FirstOrDefault(s => s.Id == shown.Id) is { } side) shown.Update(side);
        }
        Renumber();
        TourNotices.Clear();
        foreach (string notice in tour.Notices) TourNotices.Add(notice);
        TourSummary = string.Format(CultureInfo.CurrentCulture, Tr.Get("Blender_Tour_Summary"), tour.Storyboard.Shots.Count, tour.Seconds, sides.Count);
        OnPropertyChanged(nameof(HasTourBranches));
    }

    /// <summary>The side passages the one most wanted first: in the order the route says, then
    /// by rank (the rules' own order), then in the order of the tour.</summary>
    private static IEnumerable<TourRow> Ordered(List<TourRow> sides, IReadOnlyList<string>? priority)
    {
        int Said(TourRow row)
        {
            for (int i = 0; i < (priority?.Count ?? 0); i++)
                if (string.Equals(priority![i], row.Id, StringComparison.OrdinalIgnoreCase)) return i;
            return int.MaxValue;
        }
        return sides.Select((row, at) => (row, at))
            .OrderBy(x => Said(x.row)).ThenBy(x => x.row.Rank is > 0 and var rank ? rank : int.MaxValue).ThenBy(x => x.at)
            .Select(x => x.row);
    }

    private static BranchVisit VisitOf(string id, TourSpec spec)
        => spec.Exclude.Contains(id, StringComparer.OrdinalIgnoreCase) ? BranchVisit.LeaveOut
            : spec.Include.Contains(id, StringComparer.OrdinalIgnoreCase) ? BranchVisit.Visit
            : BranchVisit.Auto;

    private void Renumber()
    {
        for (int i = 0; i < TourBranches.Count; i++) TourBranches[i].Number = i + 1;
    }

    [RelayCommand]
    private void MoveBranchUp(TourBranchRowViewModel? row) => MoveBranch(row, -1);

    [RelayCommand]
    private void MoveBranchDown(TourBranchRowViewModel? row) => MoveBranch(row, +1);

    private void MoveBranch(TourBranchRowViewModel? row, int by)
    {
        if (row is null) return;
        int from = TourBranches.IndexOf(row), to = from + by;
        if (from < 0 || to < 0 || to >= TourBranches.Count) return;
        TourBranches.Move(from, to);
        Renumber();
        _branchOrderSaid = true;
        WriteTourChoices();
    }

    private void BranchVisitChanged() => WriteTourChoices();

    /// <summary>The table's choices into the shot's route: what is visited and left out by hand,
    /// and, once a row was moved, the order. What the route named that the table does not show
    /// (a station, a survey) stays.</summary>
    private void WriteTourChoices()
    {
        if (_tourShot is not { } row || row.Camera is not { Route: { } route } camera) return;
        var shown = TourBranches.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string>? Of(IReadOnlyList<string>? said, BranchVisit visit)
        {
            var list = (said ?? []).Where(e => !shown.Contains(e))
                .Concat(TourBranches.Where(b => b.Visit == visit).Select(b => b.Id)).ToList();
            return list.Count > 0 ? list : null;
        }
        route = route with
        {
            Include = Of(route.Include, BranchVisit.Visit),
            Exclude = Of(route.Exclude, BranchVisit.LeaveOut),
            Priority = _branchOrderSaid ? TourBranches.Select(b => b.Id).ToList() : route.Priority,
        };
        camera = camera with { Route = route };
        row.Shot = row.Shot with { Camera = camera };
        // The settings edit this shot: they keep its route.
        if (SelectedShot == row) _spec = _spec with { Camera = camera };
        ShotsChanged();
    }

    /// <summary>The film changed: the table, when one is shown, is worked out again.</summary>
    private void BranchTableStale()
    {
        if (_tourShot is not null && TourBranches.Count > 0) _ = PlanTourAsync(fresh: false);
    }

    private void ClearBranchTable()
    {
        _tourShot = null;
        _tourVersion++;
        TourBranches.Clear();
        TourNotices.Clear();
        TourSummary = null;
        OnPropertyChanged(nameof(HasTourBranches));
        OnPropertyChanged(nameof(IsTourShot));
        ListBranchesCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>What the user says of a side passage.</summary>
public enum BranchVisit
{
    /// <summary>As the tour's rules decide.</summary>
    Auto,

    /// <summary>Visited whatever the rules say.</summary>
    Visit,

    /// <summary>Left out whatever the rules say.</summary>
    LeaveOut,
}

/// <summary>A choice of <see cref="BranchVisit"/> in the row's list.</summary>
public sealed record BranchVisitItem(BranchVisit Value, string Label)
{
    public static IReadOnlyList<BranchVisitItem> All { get; } =
    [
        new(BranchVisit.Auto, Tr.Get("Blender_Branch_VisitAuto")),
        new(BranchVisit.Visit, Tr.Get("Blender_Branch_VisitYes")),
        new(BranchVisit.LeaveOut, Tr.Get("Blender_Branch_VisitNo")),
    ];

    public override string ToString() => Label;
}

/// <summary>A side passage of a tour as a row of the branch table.</summary>
public sealed partial class TourBranchRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private BranchVisit _visit;

    public TourBranchRowViewModel(TourRow row, BranchVisit visit, Action changed)
    {
        _row = row;
        _visit = visit;
        _changed = changed;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Id), nameof(Kind), nameof(Junction), nameof(Length), nameof(Leads), nameof(Outcome), nameof(Chosen))]
    private TourRow _row;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisitId), nameof(UpId), nameof(DownId))]
    private int _number;

    public string Id => Row.Id;

    public string VisitId => $"Blender.Branch.{Number}.Visit";
    public string UpId => $"Blender.Branch.{Number}.Up";
    public string DownId => $"Blender.Branch.{Number}.Down";

    /// <summary>A branch (one junction) or a bypass (two: a loop).</summary>
    public string Kind => Tr.Get(Row.Kind == "bypass" ? "Blender_Branch_KindBypass" : "Blender_Branch_KindBranch");

    public string Junction => Row.Junction;

    public string Length => string.Format(CultureInfo.CurrentCulture, "{0:0} m", Row.Length);

    public int Leads => Row.Leads;

    /// <summary>True when the film visits it.</summary>
    public bool Chosen => Row.Mode != "skip";

    /// <summary>What the film does about it, or why it does nothing.</summary>
    public string Outcome => Row switch
    {
        { Dropped: "budget" } => Tr.Get("Blender_Branch_DroppedBudget"),
        { Dropped: not null } => Tr.Get("Blender_Branch_DroppedNotReached"),
        { Mode: "skip" } => Tr.Get(Row.Why switch
        {
            "short" => "Blender_Branch_WhyShort",
            "overCount" => "Blender_Branch_WhyOverCount",
            "excluded" => "Blender_Branch_WhyExcluded",
            _ => "Blender_Branch_WhySkipped",
        }),
        _ => string.Format(CultureInfo.CurrentCulture, Tr.Get(Row.Mode switch
        {
            "return" => "Blender_Branch_ModeReturn",
            "through" => "Blender_Branch_ModeThrough",
            "peek" => "Blender_Branch_ModePeek",
            _ => "Blender_Branch_ModeCut",
        }), Row.Seconds) + (Row.ContinuesMeters > 0.5
            ? string.Format(CultureInfo.CurrentCulture, Tr.Get("Blender_Branch_Continues"), Row.ContinuesMeters)
            : ""),
    };

    public IReadOnlyList<BranchVisitItem> VisitChoices => BranchVisitItem.All;

    /// <summary>What the user says of it.</summary>
    public BranchVisit Visit
    {
        get => _visit;
        set
        {
            if (value == _visit) return;
            _visit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisitChoice));
            _changed();
        }
    }

    /// <summary><see cref="Visit"/> as an item of <see cref="VisitChoices"/>, for the list.</summary>
    public BranchVisitItem VisitChoice
    {
        get => BranchVisitItem.All.First(i => i.Value == Visit);
        set { if (value is not null) Visit = value.Value; }
    }

    internal void Update(TourRow row) => Row = row;
}
