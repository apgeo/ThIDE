using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Semantics;
using Therion.Workspace.Fixes;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class SyncMapRow(string scrapId, string file, IReadOnlyList<string> maps, string? chosen) : ObservableObject
{
    public string ScrapId { get; } = scrapId;
    public string File { get; } = file;
    /// <summary>The maps to choose from; the first entry means "leave it out".</summary>
    public IReadOnlyList<string> Maps { get; } = maps;
    [ObservableProperty] private string _map = chosen ?? maps[0];
}

/// <summary>Tools ▸ Add unreferenced scraps to maps: a map for each scrap no map composes.</summary>
public sealed class SyncMapsViewModel
{
    public SyncMapsViewModel(WorkspaceSemanticModel ws, string? root)
    {
        var skip = Tr.Get("SyncMaps_Skip");
        var maps = new List<string> { skip };
        maps.AddRange(ws.MapsById.Keys.Order(System.StringComparer.Ordinal));
        foreach (var id in ProjectStatistics.UnreferencedScraps(ws))
        {
            var file = ws.ScrapsById.TryGetValue(id, out var s) && s.DeclarationSpan.FilePath is { } f
                ? root is null ? Path.GetFileName(f) : Path.GetRelativePath(root, f).Replace('\\', '/')
                : "";
            Rows.Add(new SyncMapRow(id, file, maps, MapSync.DefaultMap(ws, id)));
        }
        SkipLabel = skip;
    }

    private string SkipLabel { get; }
    public ObservableCollection<SyncMapRow> Rows { get; } = new();
    public bool HasMaps => Rows.Count > 0 && Rows[0].Maps.Count > 1;

    /// <summary>Scrap → chosen map (null = skip).</summary>
    public IReadOnlyDictionary<string, string?> Assignments() =>
        Rows.ToDictionary(r => r.ScrapId, r => r.Map == SkipLabel ? null : r.Map);
}
