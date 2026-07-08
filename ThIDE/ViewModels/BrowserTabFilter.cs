// Per-tab filtering for the Object Browser.
//
// Each Object Browser tab owns a BrowserTabFilter that composes two independent filters over the
// tab's rows:
//   • a free-text filter (a single box; every whitespace-separated term must appear in *some*
//     searchable field of the row — i.e. matched additively across all fields), and
//   • an optional "custom filter": an arbitrary row predicate + a human label, pushed in from
//     elsewhere (e.g. the Overview ▸ Quality drill-downs) via ObjectBrowserViewModel.ApplyFilter.
//
// The custom filter is deliberately generic (BrowserFilter carries a Func<object,bool>), so any
// future caller can restrict a tab to any arbitrary subset of its rows without new plumbing.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ThIDE.ViewModels;

/// <summary>Identifies an Object Browser tab. The order matches the tab strip so <c>(int)</c> is the index.</summary>
public enum BrowserTab
{
    Stations, Shots, Surveys, Fixes, Equates, Maps, Scraps, Points, Lines, Areas,
}

/// <summary>
/// A custom filter targeted at one Object Browser tab: which <see cref="Tab"/> to show, a
/// human-readable <see cref="Label"/> for the "filtering by …" chip, and the row <see cref="Predicate"/>
/// selecting the subset. Generic on purpose — the predicate can describe any subset of that tab's rows.
/// </summary>
public sealed record BrowserFilter(BrowserTab Tab, string Label, Func<object, bool> Predicate);

/// <summary>Holds the text + custom filter state for a single tab and exposes the filtered <see cref="Items"/>.</summary>
public sealed partial class BrowserTabFilter : ObservableObject
{
    public BrowserTab Tab { get; }
    public BrowserTabFilter(BrowserTab tab) => Tab = tab;

    private IReadOnlyList<object> _source = Array.Empty<object>();
    private Func<object, bool>? _customPredicate;

    /// <summary>The free-text filter box content.</summary>
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>Chip text for the active custom filter (null when none is applied).</summary>
    [ObservableProperty] private string? _customLabel;

    /// <summary>True while a custom (subset) filter is applied — drives the chip + Clear button.</summary>
    public bool HasCustomFilter => _customPredicate is not null;

    /// <summary>The rows to display: the source narrowed by the custom filter and the text filter.</summary>
    public IEnumerable Items => BuildView();

    /// <summary>Replaces the backing rows (called when the browser reloads). Keeps the active filters.</summary>
    public void SetSource(IReadOnlyList<object> source)
    {
        _source = source ?? Array.Empty<object>();
        OnPropertyChanged(nameof(Items));
    }

    /// <summary>Applies a custom subset filter with its chip label (replaces any previous one).</summary>
    public void ApplyCustom(string label, Func<object, bool> predicate)
    {
        _customPredicate = predicate;
        CustomLabel = label;
        OnPropertyChanged(nameof(HasCustomFilter));
        OnPropertyChanged(nameof(Items));
    }

    /// <summary>Clears the custom subset filter (the text filter is left untouched).</summary>
    [RelayCommand]
    private void ClearCustomFilter()
    {
        if (_customPredicate is null) return;
        _customPredicate = null;
        CustomLabel = null;
        OnPropertyChanged(nameof(HasCustomFilter));
        OnPropertyChanged(nameof(Items));
    }

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(Items));

    private IEnumerable BuildView()
    {
        IEnumerable<object> query = _source;
        if (_customPredicate is { } custom) query = query.Where(custom);

        var terms = (Text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 0) query = query.Where(row => MatchesText(row, terms));

        // No active filter → hand back the source list unmaterialized (avoids copying large tables).
        return ReferenceEquals(query, _source) ? _source : query.ToList();
    }

    private static bool MatchesText(object row, string[] terms)
    {
        var haystack = BrowserRowText.Of(row);
        foreach (var term in terms)
            if (haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }
}

/// <summary>Flattens an Object Browser row into the text the free-text filter searches (its visible fields).</summary>
internal static class BrowserRowText
{
    public static string Of(object row) => row switch
    {
        StationRow r      => Join(r.QualifiedName, r.Survey),
        ShotRow r         => Join(r.From, r.To, Num(r.Length), Num(r.Compass), Num(r.Clino)),
        SurveyEntityRow r => Join(r.Name, r.Title, r.Parent, r.File),
        FixEntityRow r    => Join(r.Station, r.Coordinates, r.Cs, r.File),
        EquateEntityRow r => Join(r.Stations, r.File),
        MapEntityRow r    => Join(r.Id, r.Title, r.Projection, r.File),
        ScrapEntityRow r  => Join(r.Id, r.Sketch, r.File),
        Th2EntityRow r    => Join(r.Type, r.Scrap, r.File),
        _                 => row.ToString() ?? string.Empty,
    };

    private static string Num(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Join(params string?[] parts) => string.Join(' ', parts.Where(p => !string.IsNullOrEmpty(p)));
}
