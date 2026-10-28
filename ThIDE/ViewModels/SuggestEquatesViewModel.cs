using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Semantics;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed record EquateCandidateRow(EquateCandidate Candidate, string A, string B, string Distance, string Files);

/// <summary>Tools ▸ Suggest equates: stations close together in different pieces of the cave.</summary>
public sealed partial class SuggestEquatesViewModel : ObservableObject
{
    private readonly WorkspaceSemanticModel _ws;
    private readonly string? _root;

    public SuggestEquatesViewModel(WorkspaceSemanticModel ws, string? root)
    {
        _ws = ws;
        _root = root;
        Refresh();
    }

    public ObservableCollection<EquateCandidateRow> Rows { get; } = new();
    [ObservableProperty] private decimal _threshold = 10;
    [ObservableProperty] private EquateCandidateRow? _selected;
    [ObservableProperty] private string _note = string.Empty;

    [RelayCommand]
    public void Refresh()
    {
        Rows.Clear();
        var result = EquateCandidateFinder.Find(_ws, (double)System.Math.Max(0.1m, Threshold));
        foreach (var c in result.Candidates)
            Rows.Add(new EquateCandidateRow(c, c.A.Name.ToString(), c.B.Name.ToString(),
                c.Distance.ToString("0.00", CultureInfo.InvariantCulture) + " m",
                $"{Short(c.A.DeclarationSpan.FilePath)} · {Short(c.B.DeclarationSpan.FilePath)}"));
        Note = result.ComparablePieces < 2 ? string.Format(Tr.Get("Suggest_NeedPieces"), result.ComparablePieces)
             : Rows.Count == 0 ? Tr.Get("Suggest_NoneWithin")
             : Tr.Get("Suggest_Verify");
        Selected = Rows.Count > 0 ? Rows[0] : null;
    }

    private string Short(string? path) => path is null ? "" : _root is null ? Path.GetFileName(path) : Path.GetRelativePath(_root, path).Replace('\\', '/');
}
