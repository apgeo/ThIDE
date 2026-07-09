// Generic VS-Code-style quick-pick overlay backing model. Reused by the Go-to-File palette
// (Ctrl+P, #3) and the Command palette (Ctrl+Shift+P, #4). The list of results for a query is
// produced by an injected provider delegate, so the same control drives files, commands, and the
// per-parameter steps a command may push.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ThIDE.ViewModels.QuickPick;

/// <summary>One row in the quick-pick list.</summary>
public sealed class QuickPickItem
{
    public required string Title { get; init; }
    public string? Detail { get; init; }
    /// <summary>A native shell icon (files); takes precedence over <see cref="IconKey"/>.</summary>
    public IImage? Icon { get; init; }
    /// <summary>A DynamicResource StreamGeometry key (e.g. "Icon.Config") rendered as a PathIcon.</summary>
    public string? IconKey { get; init; }
    /// <summary>Runs when the item is accepted (Enter/click). May push another quick-pick step.</summary>
    public Func<System.Threading.Tasks.Task>? Run { get; init; }
    public object? Payload { get; init; }

    // Pre-lowered match fields (filled by providers) so filtering is allocation-light.
    public string NameLower { get; init; } = string.Empty;
    public string PathLower { get; init; } = string.Empty;
}

public partial class QuickPickViewModel : ObservableObject
{
    private readonly Func<string, IReadOnlyList<QuickPickItem>> _query;

    public string Title { get; }
    public string Watermark { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private QuickPickItem? _selected;

    public ObservableCollection<QuickPickItem> Results { get; } = new();

    /// <summary>Raised when the overlay should close (Escape, accept, or focus lost).</summary>
    public event EventHandler? CloseRequested;

    public QuickPickViewModel(string title, string watermark,
        Func<string, IReadOnlyList<QuickPickItem>> query, string initialText = "")
    {
        Title = title;
        Watermark = watermark;
        _query = query;
        _searchText = initialText;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();
        foreach (var item in _query(SearchText ?? string.Empty)) Results.Add(item);
        Selected = Results.FirstOrDefault();
    }

    public void MoveDown()
    {
        if (Results.Count == 0) return;
        int i = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[Math.Min(i + 1, Results.Count - 1)];
    }

    public void MoveUp()
    {
        if (Results.Count == 0) return;
        int i = Selected is null ? Results.Count : Results.IndexOf(Selected);
        Selected = Results[Math.Max(i - 1, 0)];
    }

    /// <summary>Rows the PageUp/PageDown keys jump by — one full page of the overlay's 15-row list.</summary>
    private const int PageSize = 15;

    public void MovePageDown()
    {
        if (Results.Count == 0) return;
        int i = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[Math.Min(i + PageSize, Results.Count - 1)];
    }

    public void MovePageUp()
    {
        if (Results.Count == 0) return;
        int i = Selected is null ? Results.Count : Results.IndexOf(Selected);
        Selected = Results[Math.Max(i - PageSize, 0)];
    }

    /// <summary>Accepts the selected item: closes the overlay, then runs its action.</summary>
    public void Accept()
    {
        var chosen = Selected;
        Close();
        if (chosen?.Run is { } run) _ = run();
    }

    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
