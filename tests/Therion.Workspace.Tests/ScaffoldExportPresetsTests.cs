using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

public class ScaffoldExportPresetsTests
{
    [Fact]
    public void Catalogue_has_the_server_outputs()
    {
        Assert.Equal(new[]
            {
                "lox", "3d", "plan-pdf", "extended-pdf", "plan-svg", "extended-svg", "kml", "dxf",
                "sql", "csv", "survey-list-html", "continuation-list-html", "shp",
            },
            ScaffoldExportPresets.Names);
    }

    [Fact]
    public void Every_preset_writes_a_distinct_output()
    {
        var options = new ScaffoldOptions { ProjectName = "cave", Exports = ScaffoldExportPresets.Resolve(ScaffoldExportPresets.Names) };
        var paths = TopodroidProjectScaffold.ExpectedOutputs(options).Select(o => o.RelativePath).ToList();

        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("rez/cave-plan.pdf", paths);
        Assert.Contains("rez/cave-extended.svg", paths);
        Assert.Contains("rez/cave-surveys.html", paths);
        Assert.Contains("rez/cave-continuations.html", paths);
        Assert.Contains("rez/cave-shp", paths);
    }

    [Fact]
    public void Resolve_keeps_the_given_order_ignores_case_and_repeats()
    {
        var items = ScaffoldExportPresets.Resolve(new[] { "PLAN-PDF", "lox", "lox" });

        Assert.Equal(2, items.Count);
        Assert.Equal(ExportKind.Map, items[0].Kind);
        Assert.Equal("plan", items[0].Projection);
        Assert.Equal("loch", items[1].Fmt);
    }

    [Fact]
    public void Unknown_names_are_reported()
    {
        Assert.False(ScaffoldExportPresets.TryResolve(new[] { "lox", "gltf" }, out var items, out var unknown));
        Assert.Single(items);
        Assert.Equal(new[] { "gltf" }, unknown);
        Assert.Throws<ArgumentException>(() => ScaffoldExportPresets.Resolve(new[] { "gltf" }));
    }

    [Fact]
    public void Thconfig_from_presets_emits_the_shapefile_and_lists_exports()
    {
        var thc = TopodroidProjectScaffold.BuildThconfig(new ScaffoldOptions
        {
            ProjectName = "cave",
            Exports = ScaffoldExportPresets.Resolve(new[] { "shp", "continuation-list-html" }),
        });

        Assert.Contains("export model -fmt esri -o \"rez/cave-shp\"", thc);
        Assert.Contains("export continuation-list -fmt html -o \"rez/cave-continuations.html\"", thc);
    }
}
