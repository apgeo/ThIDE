// Measurements document view (main panel): a filterable / sortable / groupable
// projection of the centreline data legs produced by the semantic binder.
// Sorting is handled natively by the DataGrid over the DataGridCollectionView;
// filtering and grouping are driven from this ViewModel.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Semantics;

namespace TherionProc.ViewModels;

/// <summary>One measurement (data leg) projected for the Measurements grid.</summary>
public sealed class MeasurementRow
{
    public string Survey { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public double? Length { get; init; }
    public double? Compass { get; init; }
    public double? Clino { get; init; }

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

public partial class MeasurementsViewModel : ViewModelBase
{
    public const string GroupByNone   = "None";
    public const string GroupBySurvey = "Survey";
    public const string GroupByStyle  = "Style";
    public const string GroupByFlags  = "Flags";

    private readonly List<MeasurementRow> _all = new();

    [ObservableProperty] private DataGridCollectionView? _view;
    [ObservableProperty] private int _count;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _groupBy = GroupByNone;

    public IReadOnlyList<string> GroupByOptions { get; } =
        new[] { GroupByNone, GroupBySurvey, GroupByStyle, GroupByFlags };

    partial void OnFilterTextChanged(string value)
    {
        View?.Refresh();
        UpdateCount();
    }

    partial void OnGroupByChanged(string value) => ApplyGrouping();

    /// <summary>Loads measurements from a single-file semantic model.</summary>
    public void Load(SemanticModel model) => Build(Project(model.Shots));

    /// <summary>Loads measurements from every per-file model in a workspace snapshot.</summary>
    public void Load(WorkspaceSemanticModel workspace) =>
        Build(workspace.PerFile.Values.SelectMany(m => Project(m.Shots)));

    private static IEnumerable<MeasurementRow> Project(ImmutableArray<ShotSymbol> shots)
    {
        foreach (var s in shots)
        {
            var f = s.Flags;
            yield return new MeasurementRow
            {
                Survey      = s.From.HasParent ? s.From.Parent().ToString() : string.Empty,
                From        = s.From.ToString(),
                To          = s.To.ToString(),
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

    private void Build(IEnumerable<MeasurementRow> rows)
    {
        _all.Clear();
        _all.AddRange(rows);

        var view = new DataGridCollectionView(_all) { Filter = FilterRow };
        View = view;
        ApplyGrouping();
        TotalCount = _all.Count;
        UpdateCount();
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

    private bool FilterRow(object o)
    {
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        if (o is not MeasurementRow r) return true;
        var q = FilterText.Trim();
        return Contains(r.From, q) || Contains(r.To, q) || Contains(r.Survey, q)
            || Contains(r.Flags, q) || Contains(r.Comment, q) || Contains(r.Style, q);
    }

    private void UpdateCount() =>
        Count = string.IsNullOrWhiteSpace(FilterText) ? _all.Count : _all.Count(FilterRow);

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null &&
        haystack.Contains(needle, System.StringComparison.OrdinalIgnoreCase);

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
