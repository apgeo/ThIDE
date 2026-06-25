// Isolation repro for the "map/scrap identifiers inside a `map` command are not
// navigable" report (batch item #4). Loads the real PS-1intrare corpus project via
// its thconfig and asserts that the `.th2` sketch is pulled in, its scraps are indexed,
// and the bare scrap ids written inside the `map MP-ps1a … endmap` block resolve to
// their scrap declarations in the `.th2`.

using System.IO;
using System.Linq;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Workspace;

namespace Therion.Workspace.Tests;

public class MapScrapNavigationTests
{

    private static string? CorpusFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var segs = new[] { dir.FullName, "tests", "Corpus", "sample_projects",
                "Therion_202502x", "500", "PS-1intrare" }.Concat(parts).ToArray();
            var candidate = Path.Combine(segs);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task Thconfig_pulls_in_th2_and_indexes_its_scraps()
    {
        var cfg = CorpusFile("thconfig_PS1x-all_500.thc");
        if (cfg is null) return; // corpus not present in this environment

        await using var ws = new TherionWorkspace(options: new WorkspaceOptions { DisableDiskCache = true });
        await ws.LoadAsync(cfg);

        // The .th input chain: thconfig → PS1x-all_500.th → 20150926_ovi/20150926_ps1.th → 20150926_ps1.th2
        Assert.Contains(ws.LoadedFiles, p =>
            p.EndsWith("20150926_ps1.th", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ws.LoadedFiles, p =>
            p.EndsWith("20150926_ps1.th2", StringComparison.OrdinalIgnoreCase));

        var model = ws.BuildSemanticModel();

        // The scraps declared in the .th2 must be in the cross-file index.
        Assert.True(model.ScrapsById.ContainsKey("ps101-intrare"),
            "ps101-intrare scrap should be indexed from the .th2");
        Assert.True(model.ScrapsById.ContainsKey("ps102-pana_la_C1"),
            "ps102-pana_la_C1 scrap should be indexed from the .th2");
    }

    [Fact]
    public async Task Map_body_scrap_ids_resolve_to_th2_declarations()
    {
        var cfg = CorpusFile("thconfig_PS1x-all_500.thc");
        if (cfg is null) return;

        await using var ws = new TherionWorkspace(options: new WorkspaceOptions { DisableDiskCache = true });
        await ws.LoadAsync(cfg);
        var model = ws.BuildSemanticModel();

        // `ps101-intrare` is written bare inside `map MP-ps1a` in 20150926_ps1.th and must
        // jump to its `scrap ps101-intrare` declaration in the sibling .th2.
        var span = model.ResolveReference("ps101-intrare", ReferenceKind.ScrapObject);
        Assert.NotNull(span);
        Assert.EndsWith("20150926_ps1.th2", span!.Value.FilePath, StringComparison.OrdinalIgnoreCase);

        // Also via the navigation service the editor actually uses, with the .th as active file.
        var thPath = ws.LoadedFiles.First(p => p.EndsWith("20150926_ps1.th", StringComparison.OrdinalIgnoreCase));
        var nav = new WorkspaceSymbolNavigationService(model, activeFilePath: thPath);
        Assert.True(nav.CanNavigate("ps102-pana_la_C1", ReferenceKind.ScrapObject),
            "map-body scrap id should be navigable");
    }
}
