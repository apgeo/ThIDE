// Phase 1 — recursive project traversal: `input` directives nested inside survey
// blocks must be followed (the grind project links every date/*.th this way).

using System.IO;
using System.Linq;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Workspace;

namespace Therion.Workspace.Tests;

public class RecursiveTraversalTests
{
    [Fact]
    public async Task Load_follows_input_nested_inside_survey_block()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        var cfg = Path.Combine(dir, "proj.thconfig");
        var main = Path.Combine(dir, "main.th");
        var child = Path.Combine(dir, "sub", "child.th");

        await File.WriteAllTextAsync(cfg, "source main.th\n");
        // `input` is *inside* the survey block, the way real Therion projects nest it.
        await File.WriteAllTextAsync(main, "survey s\n  input sub/child.th\nendsurvey\n");
        await File.WriteAllTextAsync(child, "survey child\nendsurvey\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);

        Assert.Contains(ws.LoadedFiles, p =>
            string.Equals(p, Path.GetFullPath(child), StringComparison.OrdinalIgnoreCase));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Load_resolves_backslash_separated_input_paths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "date"));
        var main = Path.Combine(dir, "main.th");
        var child = Path.Combine(dir, "date", "leaf.th");

        // Windows-style backslash path (as exported by TopoDroid / used in grind.th).
        await File.WriteAllTextAsync(main, "survey s\n  input date\\leaf.th\nendsurvey\n");
        await File.WriteAllTextAsync(child, "survey leaf\nendsurvey\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(main);

        Assert.Contains(ws.LoadedFiles, p =>
            string.Equals(p, Path.GetFullPath(child), StringComparison.OrdinalIgnoreCase));

        Directory.Delete(dir, recursive: true);
    }

    // ---- grind corpus integration (skipped when the corpus isn't present) ----

    private static string? GrindThconfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Corpus", "sample_projects",
                "grind", "therion", "thconfig_grind2025.thconfig");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task Grind_project_reaches_nested_date_files_and_resolves_cross_refs()
    {
        var cfg = GrindThconfig();
        if (cfg is null) return; // corpus not checked out in this environment

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);

        // Every `input date\...\*.th` nested in `survey grind` must be reached.
        var dateFiles = ws.LoadedFiles
            .Where(p => p.Replace('\\', '/').Contains("/date/") &&
                        p.EndsWith(".th", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(dateFiles.Count > 20,
            $"Expected many date/*.th files, found {dateFiles.Count}.");
        Assert.Contains(ws.LoadedFiles, p =>
            p.EndsWith("grind_175_baza_niagara.th", StringComparison.OrdinalIgnoreCase));

        var model = ws.BuildSemanticModel();

        // Station ref `11@grind_175_baza_niagara` → station def in that file.
        var station = model.ResolveReference("11@grind_175_baza_niagara", ReferenceKind.Station);
        Assert.NotNull(station);
        Assert.EndsWith("grind_175_baza_niagara.th", station!.Value.FilePath);

        // Survey ref → survey decl.
        var survey = model.ResolveReference("grind_wg_superior_meandru", ReferenceKind.Survey);
        Assert.NotNull(survey);
        Assert.EndsWith("grind_wg_superior_meandru.th", survey!.Value.FilePath);
    }
}
