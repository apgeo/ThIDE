using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ThIDE.ViewModels.Pickers;

/// <summary>One row of a picker. <see cref="Payload"/> is what the owner gets back on selection.</summary>
/// <param name="Depth">Indentation level, for lists that read as a tree.</param>
public sealed record PickerItem(string Primary, string? Secondary, string SearchText, int Depth, object Payload)
{
    public double Indent => Depth * 12;
}

/// <summary>
/// A filterable list with a single selection: a text box narrows the rows (every typed word must
/// appear, case-insensitively), the list can be folded away, and the choice is reported through
/// <see cref="SelectionChanged"/>. Station and survey pickers build on it.
/// </summary>
public partial class ItemPickerViewModel : ObservableObject
{
    private IReadOnlyList<PickerItem> _all = Array.Empty<PickerItem>();
    private bool _quiet;

    /// <param name="name">Used for automation ids: <c>Picker.&lt;name&gt;.Filter</c>, <c>.List</c>, <c>.Toggle</c>.</param>
    public ItemPickerViewModel(string name) => Name = name;

    public string Name { get; }
    public string FilterId => $"Picker.{Name}.Filter";
    public string ListId => $"Picker.{Name}.List";
    public string ToggleId => $"Picker.{Name}.Toggle";

    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private IReadOnlyList<PickerItem> _visible = Array.Empty<PickerItem>();
    [ObservableProperty] private PickerItem? _selected;
    [ObservableProperty] private bool _isListOpen = true;

    /// <summary>
    /// The list's selection. Separate from <see cref="Selected"/> because the list clears its
    /// selection whenever the filter hides the chosen row — that must not undo the choice.
    /// </summary>
    public PickerItem? ListSelection
    {
        get => Selected is { } s && Visible.Contains(s) ? s : null;
        set { if (value is not null) Selected = value; }
    }

    /// <summary>Raised when the selection changes (also when cleared).</summary>
    public event EventHandler<PickerItem?>? SelectionChanged;

    public IReadOnlyList<PickerItem> Items => _all;
    public int Count => _all.Count;

    /// <summary>Replaces the rows. The selection survives when its payload is still there.</summary>
    public void SetItems(IReadOnlyList<PickerItem> items)
    {
        var keep = Selected?.Payload;
        _all = items;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Count));
        ApplyFilter();
        var again = keep is null ? null : _all.FirstOrDefault(i => Equals(i.Payload, keep));
        if (!ReferenceEquals(again, Selected)) Selected = again;
    }

    /// <summary>Selects the first row whose payload matches; returns false (selection unchanged) when none does.</summary>
    public bool SelectByPayload(Func<object, bool> match)
    {
        var item = _all.FirstOrDefault(i => match(i.Payload));
        if (item is null) return false;
        if (!Visible.Contains(item))
        {
            // Make the choice visible even when the filter hid it.
            _quiet = true;
            Filter = string.Empty;
            _quiet = false;
            ApplyFilter();
        }
        Selected = item;
        return true;
    }

    [RelayCommand] private void ToggleList() => IsListOpen = !IsListOpen;

    partial void OnFilterChanged(string value)
    {
        if (!_quiet) ApplyFilter();
        IsListOpen = true;
    }

    partial void OnVisibleChanged(IReadOnlyList<PickerItem> value) => OnPropertyChanged(nameof(ListSelection));

    partial void OnSelectedChanged(PickerItem? value)
    {
        OnPropertyChanged(nameof(ListSelection));
        OnPropertyChanged(nameof(SelectionText));
        SelectionChanged?.Invoke(this, value);
    }

    /// <summary>"Selected: 12" under the list, or a hint that nothing is chosen yet.</summary>
    public string SelectionText => Selected is { } s
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, ThIDE.Resources.Tr.Get("Picker_SelectedFormat"),
            s.Secondary is { Length: > 0 } sec ? $"{s.Primary} ({sec})" : s.Primary)
        : ThIDE.Resources.Tr.Get("Picker_NoneSelected");

    private void ApplyFilter() => Visible = Matches(_all, Filter);

    /// <summary>Rows containing every whitespace-separated term of <paramref name="filter"/> (case-insensitive).</summary>
    public static IReadOnlyList<PickerItem> Matches(IReadOnlyList<PickerItem> items, string? filter)
    {
        var terms = (filter ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return items;
        return items.Where(i => terms.All(t => i.SearchText.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

/// <summary>Converters the picker views need.</summary>
public static class PickerConverters
{
    /// <summary>A number of pixels to a left-only margin (tree indentation).</summary>
    public static Avalonia.Data.Converters.IValueConverter LeftMargin { get; } =
        new Avalonia.Data.Converters.FuncValueConverter<double, Avalonia.Thickness>(px => new Avalonia.Thickness(px, 0, 0, 0));
}
