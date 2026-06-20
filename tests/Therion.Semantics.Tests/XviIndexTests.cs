// M4 — XviIndex tests: cross-file resolution + diagnostics with injected file probe.
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class XviIndexTests
{
    private static XviFile MakeXvi(string path, string image, AffineTransform2D? t = null) =>
        new(SourceSpan.None, path, 1, 200, t ?? new AffineTransform2D(1, 0, 0, 1, 0, 0),
            image, ImmutableArray<CalibrationPoint>.Empty,
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
    public void Missing_image_emits_TH_XVI_001()
    {
        var dir = System.IO.Path.GetFullPath("xvitest");
        var xviPath = System.IO.Path.Combine(dir, "a.xvi");
        var xvi = MakeXvi(xviPath, "missing.jpg");
        var idx = XviIndex.Build(new[] { xvi }, System.Array.Empty<TherionFile>(),
            _ => false);
        Assert.Contains(idx.Diagnostics,
            d => d.Code == SemanticDiagnosticCodes.XviImageMissing);
    }

    [Fact]
    public void Missing_xvi_referenced_from_scrap_emits_TH_XVI_002()
    {
        var th2 = MakeTh2WithSketch("/p/draw.th2", "bg.xvi");
        var idx = XviIndex.Build(System.Array.Empty<XviFile>(), new[] { th2 },
            _ => false);
        Assert.Contains(idx.Diagnostics,
            d => d.Code == SemanticDiagnosticCodes.XviFileMissing);
        Assert.Single(idx.FileGraphEdges);
    }

    [Fact]
    public void Degenerate_transform_emits_TH_XVI_003()
    {
        var xvi = MakeXvi("/p/a.xvi", "img.jpg",
            new AffineTransform2D(0, 0, 0, 0, 0, 0));
        var idx = XviIndex.Build(new[] { xvi }, System.Array.Empty<TherionFile>(),
            _ => true); // image exists
        Assert.Contains(idx.Diagnostics,
            d => d.Code == SemanticDiagnosticCodes.XviTransformDegenerate);
    }

    [Fact]
    public void Resolves_referencing_scraps_back_to_xvi_symbol()
    {
        var dir = System.IO.Path.GetFullPath("p");
        var xviPath = System.IO.Path.Combine(dir, "bg.xvi");
        var xvi = MakeXvi(xviPath, "img.jpg");
        var th2Path = System.IO.Path.Combine(dir, "draw.th2");
        var th2 = MakeTh2WithSketch(th2Path, "bg.xvi");
        var idx = XviIndex.Build(new[] { xvi }, new[] { th2 }, _ => true);
        var sym = idx.ByPath[xviPath];
        Assert.Single(sym.ReferencingScraps);
    }
}
