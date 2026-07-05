// Implementation Plan �7.3 � XVI References tab in Object Browser.
// One row per indexed .xvi file with summary metadata + referencing scrap count.

using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Semantics;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public sealed record XviReferenceRow(
    string XviPath,
    int Stations,
    int Shots,
    int SketchLines,
    string Grid,
    int ReferencingScraps);

public partial class XviReferencesViewModel : ViewModelBase
{
    private readonly IDocumentService? _documents;

    [ObservableProperty]
    private IReadOnlyList<XviReferenceRow> _rows = System.Array.Empty<XviReferenceRow>();

    public XviReferencesViewModel() { }
    public XviReferencesViewModel(IDocumentService documents)
    {
        _documents = documents;
        _documents.DocumentChanged += (_, _) => Refresh();
    }

    public void Refresh() => Load(_documents?.Workspace);

    public void Load(WorkspaceSemanticModel? workspace)
    {
        if (workspace is null) { Rows = System.Array.Empty<XviReferenceRow>(); return; }

        Rows = workspace.Xvi.ByPath.Values
            .Select(x => new XviReferenceRow(
                x.ResolvedXviPath,
                x.File.Stations.Length,
                x.File.Shots.Length,
                x.File.SketchLines.Length,
                x.File.Grid is { } g ? $"{g.CountX}×{g.CountY}" : "—",
                x.ReferencingScraps.Length))
            .OrderBy(r => r.XviPath, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
