// XviIndex tests: cross-file .th2 -> sketch-target resolution with an injected file probe.
using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class XviIndexTests
{
    private static XviFile MakeXvi(string path) =>
        new(SourceSpan.None, path, 1.0, "m",
            ImmutableArray<XviStation>.Empty, ImmutableArray<XviShot>.Empty,
            ImmutableArray<XviSketchLine>.Empty, null,
            ImmutableArray<TrivialComment>.Empty);

    private static TherionFile MakeTh2WithSketch(string th2Path, string xviPath)
    {
        var scrap = new ScrapBlock(SourceSpan.None, "s", string.Empty,
            ImmutableArray.Create(new SketchReference(SourceSpan.None, xviPath, 0, 0)),
            ImmutableArray<TherionNode>.Empty, true);
        return new TherionFile(SourceSpan.None, th2Path,
            ImmutableArray.Create<TherionNode>(scrap),
            TherionSyntaxVersion.Default);
    }

    [Fact]
    public void Missing_sketch_target_referenced_from_scrap_emits_TH_XVI_050()
    {
        var th2 = MakeTh2WithSketch("/p/draw.th2", "bg.xvi");
        var idx = XviIndex.Build(System.Array.Empty<XviFile>(), new[] { th2 },
            _ => false);
        Assert.Contains(idx.Diagnostics,
            d => d.Code == SemanticDiagnosticCodes.XviFileMissing);
        Assert.Single(idx.FileGraphEdges);
    }

    [Fact]
    public void Existing_sketch_target_emits_no_diagnostic()
    {
        var th2 = MakeTh2WithSketch("/p/draw.th2", "bg.xvi");
        var idx = XviIndex.Build(System.Array.Empty<XviFile>(), new[] { th2 }, _ => true);
        Assert.Empty(idx.Diagnostics);
    }

    [Fact]
    public void Resolves_referencing_scraps_back_to_xvi_symbol()
    {
        var dir = System.IO.Path.GetFullPath("p");
        var xviPath = System.IO.Path.Combine(dir, "bg.xvi");
        var xvi = MakeXvi(xviPath);
        var th2Path = System.IO.Path.Combine(dir, "draw.th2");
        var th2 = MakeTh2WithSketch(th2Path, "bg.xvi");
        var idx = XviIndex.Build(new[] { xvi }, new[] { th2 }, _ => true);
        var sym = idx.ByPath[xviPath];
        Assert.Single(sym.ReferencingScraps);
    }
}
