// The guide half of the "Derive survey from 3-D model" dialog: an optional start point, the points
// and legs the user gives, and the live check of every leg against the model. A leg is checked
// again, off the UI thread and after a short pause, whenever one of its points moves.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Cave3D;
using Therion.MeshSurvey;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.Workspace.Import;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class MeshSurveyViewModel
{
    public IReadOnlyList<Choice<GuideSnap>> SnapChoices { get; } = MeshSurveyChoices.Of<GuideSnap>("Snap");
    public IReadOnlyList<Choice<GuideMode>> GuideModes { get; } = MeshSurveyChoices.Of<GuideMode>("GuideMode");
    public IReadOnlyList<Choice<GuideBlockedPolicy>> BlockedPolicies { get; } = MeshSurveyChoices.Of<GuideBlockedPolicy>("Blocked");
    public IReadOnlyList<Choice<GuideFrame>> FrameChoices { get; } = MeshSurveyChoices.Of<GuideFrame>("Frame");

    /// <summary>Unchecked "automatic" = the start point below is used.</summary>
    [ObservableProperty] private bool _startAutomatic = true;
    [ObservableProperty] private GuideMode _guideMode = GuideMode.Complete;
    [ObservableProperty] private GuideBlockedPolicy _onBlocked = GuideBlockedPolicy.Error;
    [ObservableProperty] private GuideFrame _guideFrame = GuideFrame.Model;

    public GuidePointRow StartPoint { get; }
    public ObservableCollection<GuidePointRow> Points { get; } = [];
    public ObservableCollection<GuideLegRow> Legs { get; } = [];

    /// <summary>The names of the points a leg can start or end at, for the leg rows' combo boxes.</summary>
    public ObservableCollection<string> PointNames { get; } = [];

    /// <summary>How long a leg waits after the last change before it is checked.</summary>
    public TimeSpan CheckDebounce { get; }

    private readonly List<Task> _pendingChecks = [];

    private GuidePointRow Hook(GuidePointRow row)
    {
        row.Renamed = OnPointRenamed;
        row.Moved = OnPointMoved;
        return row;
    }

    private GuideLegRow Hook(GuideLegRow row)
    {
        row.Changed = ScheduleCheck;
        return row;
    }

    partial void OnStartAutomaticChanged(bool value) => GuideStructureChanged(recheck: true);
    partial void OnGuideFrameChanged(GuideFrame value) => RecheckAllLegs();
    partial void OnGuideModeChanged(GuideMode value) => RefreshIssues();

    private void OnPointRenamed(GuidePointRow row, string oldName)
    {
        foreach (var leg in Legs)
        {
            // Not a rename of the other end's duplicate: only follow the old name.
            if (leg.From == oldName) leg.From = row.Name;
            if (leg.To == oldName) leg.To = row.Name;
        }
        GuideStructureChanged(recheck: false);
    }

    private void OnPointMoved(GuidePointRow row)
    {
        foreach (var leg in Legs)
            if (leg.From == row.Name || leg.To == row.Name) ScheduleCheck(leg);
        RefreshIssues();
    }

    private void GuideStructureChanged(bool recheck)
    {
        var names = ActivePoints().Select(p => p.Name).ToList();
        if (!names.SequenceEqual(PointNames))
        {
            PointNames.Clear();
            foreach (var n in names) PointNames.Add(n);
        }
        if (recheck) RecheckAllLegs();
        RefreshIssues();
    }

    /// <summary>The start point (when it is used) and the others, in order.</summary>
    private IEnumerable<GuidePointRow> ActivePoints() => StartAutomatic ? Points : Points.Prepend(StartPoint);

    private string NextPointName()
    {
        for (int i = Points.Count + 1; ; i++)
        {
            var name = "p" + i.ToString(CultureInfo.InvariantCulture);
            if (!ActivePoints().Any(p => p.Name == name) && StartPoint.Name != name) return name;
        }
    }

    // ----- editing the lists -----

    [RelayCommand]
    public void AddPoint()
    {
        var row = Hook(new GuidePointRow(NextPointName()));
        Points.Add(row);
        GuideStructureChanged(recheck: false);
    }

    [RelayCommand]
    public void RemovePoint(GuidePointRow? row)
    {
        if (row is null || !Points.Remove(row)) return;
        GuideStructureChanged(recheck: false);
        foreach (var leg in Legs)
            if (leg.From == row.Name || leg.To == row.Name) ScheduleCheck(leg);
    }

    [RelayCommand]
    public void MovePointUp(GuidePointRow? row) => MovePoint(row, -1);

    [RelayCommand]
    public void MovePointDown(GuidePointRow? row) => MovePoint(row, 1);

    private void MovePoint(GuidePointRow? row, int delta)
    {
        if (row is null) return;
        int i = Points.IndexOf(row), j = i + delta;
        if (i < 0 || j < 0 || j >= Points.Count) return;
        Points.Move(i, j);
        GuideStructureChanged(recheck: false);
    }

    [RelayCommand]
    public void AddLeg()
    {
        var names = ActivePoints().Select(p => p.Name).ToList();
        var leg = Hook(new GuideLegRow { From = names.Count > 0 ? names[^1] : "", To = "" });
        Legs.Add(leg);
        ScheduleCheck(leg);
        RefreshIssues();
    }

    [RelayCommand]
    public void RemoveLeg(GuideLegRow? row)
    {
        if (row is null) return;
        row.Cts?.Cancel();
        Legs.Remove(row);
        RefreshIssues();
    }

    /// <summary>Adds a leg between every two consecutive points (the start first, when it is used); a pair that has a leg already is left alone.</summary>
    [RelayCommand]
    public void ChainPoints()
    {
        var names = ActivePoints().Select(p => p.Name).ToList();
        for (int i = 0; i + 1 < names.Count; i++)
        {
            string a = names[i], b = names[i + 1];
            if (Legs.Any(l => (l.From == a && l.To == b) || (l.From == b && l.To == a))) continue;
            var leg = Hook(new GuideLegRow { From = a, To = b });
            Legs.Add(leg);
            ScheduleCheck(leg);
        }
        RefreshIssues();
    }

    // ----- the guide as the library reads it -----

    /// <summary>The problems of the guide fields: unreadable coordinates and inconsistent names.</summary>
    private IEnumerable<string> GuideIssues()
    {
        var issues = new List<string>();
        foreach (var p in ActivePoints())
            if (!p.TryGetPosition(out _))
                issues.Add(string.Format(CultureInfo.InvariantCulture, Tr.Get("MeshSurvey_Issue_PointCoords"), p.Name));
        issues.AddRange(BuildGuide().Validate());
        return issues;
    }

    /// <summary>The guide from the fields; a point whose coordinates cannot be read is left out (the issues say so).</summary>
    public SurveyGuide BuildGuide()
    {
        GuidePoint? Make(GuidePointRow p) =>
            p.TryGetPosition(out var pos) ? new GuidePoint { Name = p.Name.Trim(), Position = pos, Snap = p.Snap } : null;
        return new SurveyGuide
        {
            Start = StartAutomatic ? null : Make(StartPoint),
            Points = Points.Select(Make).OfType<GuidePoint>().ToList(),
            Legs = Legs.Select(l => new GuideLeg { From = l.From.Trim(), To = l.To.Trim(), Force = l.Force }).ToList(),
            Mode = GuideMode,
            OnBlocked = OnBlocked,
            Frame = GuideFrame,
        };
    }

    /// <summary>Replaces the guide fields with a guide read from a file.</summary>
    public void LoadGuide(SurveyGuide guide)
    {
        foreach (var l in Legs) l.Cts?.Cancel();
        Points.Clear();
        Legs.Clear();
        StartAutomatic = guide.Start is null;
        if (guide.Start is { } s) CopyTo(StartPoint, s);
        foreach (var p in guide.Points) Points.Add(Hook(CopyTo(new GuidePointRow(p.Name), p)));
        GuideMode = guide.Mode;
        OnBlocked = guide.OnBlocked;
        GuideFrame = guide.Frame;
        foreach (var l in guide.Legs) Legs.Add(Hook(new GuideLegRow { From = l.From, To = l.To, Force = l.Force }));
        GuideStructureChanged(recheck: true);
    }

    private GuidePointRow CopyTo(GuidePointRow row, GuidePoint p)
    {
        row.Moved = null;
        row.Name = p.Name;
        row.SetPosition(p.Position);
        row.Snap = p.Snap;
        row.Moved = OnPointMoved;
        return row;
    }

    [RelayCommand]
    public async Task LoadGuideFileAsync()
    {
        if (_picker is null) return;
        var path = await _picker.PickOpenFileAsync(Tr.Get("MeshSurvey_LoadGuideTitle")).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var (guide, warnings) = await Task.Run(() => MeshSurveyImport.ReadGuide(path)).ConfigureAwait(true);
            LoadGuide(guide);
            if (warnings.Count > 0) _notifications?.Warning(Tr.Get("MeshSurvey_Title"), string.Join("\n", warnings));
        }
        catch (Exception ex) { Fail(ex); }
    }

    [RelayCommand]
    public async Task SaveGuideFileAsync()
    {
        if (_picker is null) return;
        var path = await _picker.PickSaveFileAsync(Tr.Get("MeshSurvey_SaveGuideTitle"), "guide.json", ModelFolder).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var json = MeshSurveyJson.Serialize(BuildGuide());
            await Task.Run(() => System.IO.File.WriteAllText(path, json)).ConfigureAwait(true);
            _notifications?.Success(Tr.Get("MeshSurvey_Title"), path);
        }
        catch (Exception ex) { Fail(ex); }
    }

    // ----- the live leg check -----

    private void RecheckAllLegs()
    {
        foreach (var leg in Legs) ScheduleCheck(leg);
    }

    private void ScheduleCheck(GuideLegRow leg)
    {
        leg.Cts?.Cancel();
        var cts = leg.Cts = new CancellationTokenSource();
        if (_session is null)
        {
            leg.SetNone(Tr.Get("MeshSurvey_Leg_NoModel"));
            return;
        }
        leg.SetPending();
        var task = CheckLegAsync(leg, _session, cts.Token);
        lock (_pendingChecks) _pendingChecks.Add(task);
        _ = task.ContinueWith(t => { lock (_pendingChecks) _pendingChecks.Remove(t); }, TaskScheduler.Default);
    }

    private async Task CheckLegAsync(GuideLegRow leg, MeshSurveySession session, CancellationToken ct)
    {
        try
        {
            if (CheckDebounce > TimeSpan.Zero) await Task.Delay(CheckDebounce, ct).ConfigureAwait(true);
            else await Task.Yield();
            ct.ThrowIfCancellationRequested();

            var from = ActivePoints().FirstOrDefault(p => p.Name == leg.From);
            var to = ActivePoints().FirstOrDefault(p => p.Name == leg.To);
            if (from is null || to is null || ReferenceEquals(from, to) || !from.TryGetPosition(out var a) || !to.TryGetPosition(out var b))
            {
                leg.SetNone(Tr.Get("MeshSurvey_Leg_NeedPoints"));
                return;
            }
            if (GuideFrame == GuideFrame.Source)
            {
                a = session.Model.SourceToModel(a);
                b = session.Model.SourceToModel(b);
            }
            var options = new LegCheckOptions { Clearance = GuidePointRow.TryParse(LegClearance, out var c) && c >= 0 ? c : 0.15 };
            var check = await Task.Run(() => session.CheckLeg(a, b, options), ct).ConfigureAwait(true);
            if (!ct.IsCancellationRequested) leg.SetCheck(check);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { leg.SetNone(ex.Message); }
    }

    /// <summary>Completes when every leg check that was scheduled so far (and any scheduled meanwhile) has finished.</summary>
    public async Task WaitForLegChecksAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_pendingChecks) tasks = _pendingChecks.ToArray();
            if (tasks.Length == 0) return;
            await Task.WhenAll(tasks).ConfigureAwait(true);
            await Task.Yield();
        }
    }
}
