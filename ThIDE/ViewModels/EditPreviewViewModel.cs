using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace ThIDE.ViewModels;

public sealed partial class EditPreviewItem(EditPreviewRow row, string location) : ObservableObject
{
    public EditPreviewRow Row { get; } = row;
    public string Location { get; } = location;
    public string Before => Row.Before;
    public string After => Row.After;
    [ObservableProperty] private bool _isChecked = true;
}

/// <summary>A list of proposed edits to tick before applying (fix all, sync maps, format all).</summary>
public sealed partial class EditPreviewViewModel : ObservableObject
{
    private readonly EditBatch _batch;

    /// <param name="allOrNothing">The edits only make sense together (a renumbering): no unticking.</param>
    public EditPreviewViewModel(string title, string summary, EditBatch batch, IReadOnlyList<EditPreviewRow> rows, string? root, bool allOrNothing = false)
    {
        Title = title;
        CanChoose = !allOrNothing;
        Summary = summary;
        _batch = batch;
        foreach (var r in rows)
        {
            var file = root is null ? Path.GetFileName(r.FilePath) : Path.GetRelativePath(root, r.FilePath).Replace('\\', '/');
            var item = new EditPreviewItem(r, $"{file}:{r.Line}");
            item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CheckedCount));
            Items.Add(item);
        }
    }

    public string Title { get; }
    public string Summary { get; }
    public bool CanChoose { get; }
    public ObservableCollection<EditPreviewItem> Items { get; } = new();
    public int CheckedCount => Items.Count(i => i.IsChecked);

    /// <summary>The batch with only the ticked edits.</summary>
    public EditBatch Chosen() => FixAll.Keep(_batch, Items.Where(i => i.IsChecked).Select(i => i.Row));
}
