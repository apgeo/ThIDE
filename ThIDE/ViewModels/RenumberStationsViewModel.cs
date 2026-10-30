using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Core;
using Therion.Semantics;
using Therion.Workspace.Fixes;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

/// <summary>
/// Tools ▸ Renumber stations: a new prefix and/or a number shift for every station of one survey,
/// planned live so the problems (duplicates, clashes) show before the preview.
/// </summary>
public sealed partial class RenumberStationsViewModel : ObservableObject
{
    private readonly WorkspaceSemanticModel _ws;
    private readonly Func<string, string?> _readText;

    public RenumberStationsViewModel(WorkspaceSemanticModel ws, Func<string, string?> readText, string? survey = null)
    {
        _ws = ws;
        _readText = readText;
        Surveys = ws.SurveysByFullName.Keys.Order(NaturalStringComparer.Instance).ToList();
        _survey = survey is not null && Surveys.Contains(survey) ? survey : Surveys.FirstOrDefault();
        Recompute();
    }

    public IReadOnlyList<string> Surveys { get; }
    [ObservableProperty] private string? _survey;
    [ObservableProperty] private string _oldPrefix = "";
    [ObservableProperty] private string _newPrefix = "";
    [ObservableProperty] private decimal _offset;
    [ObservableProperty] private bool _includeSubsurveys;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _canApply;
    public ObservableCollection<string> Examples { get; } = new();
    public ObservableCollection<string> Problems { get; } = new();

    public RenumberPlan? Plan { get; private set; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Survey) or nameof(OldPrefix) or nameof(NewPrefix) or nameof(Offset) or nameof(IncludeSubsurveys))
            Recompute();
    }

    private void Recompute()
    {
        Examples.Clear();
        Problems.Clear();
        Plan = null;
        CanApply = false;
        var rule = new RenumberRule(OldPrefix?.Trim() ?? "", NewPrefix?.Trim() ?? "", (int)Offset);
        if (Survey is null || rule.IsNoOp) { Summary = Tr.Get("Renumber_Nothing"); return; }

        var plan = StationRenumber.Plan(_ws, Survey, rule, IncludeSubsurveys, _readText);
        foreach (var (station, newName) in plan.Renames.Take(40))
            Examples.Add($"{Shown(station)}  →  {newName}");
        if (plan.Renames.Count > 40) Examples.Add("…");
        foreach (var p in plan.Problems.Take(20))
            Problems.Add(string.Format(Tr.Get("Renumber_" + p.Kind), p.Detail, p.Station is null ? "" : Shown(QualifiedName.Parse(p.Station))));
        Summary = string.Format(Tr.Get("Renumber_Summary"), plan.Renames.Count, plan.Unchanged.Count, plan.Batch.Edits.Sum(g => g.Edits.Count), plan.Batch.Edits.Count);
        Plan = plan;
        CanApply = plan.Ok;
    }

    // The station as written from inside its survey's parent: 7@upper.
    private string Shown(QualifiedName station) =>
        station.HasParent && Survey is not null && station.Parent().ToString() != Survey
            ? station.Last + "@" + string.Join('.', station.Parent().Parts.Skip(QualifiedName.Parse(Survey).Parts.Length).Reverse())
            : station.Last;
}
