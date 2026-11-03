using System.Collections.Generic;
using System.IO;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using ThIDE.Services;

namespace ThIDE.Tests;

// the referenced-scan pass of the media manager.
public class MediaScannerTests
{
    [Fact]
    public void Null_workspace_returns_empty()
    {
        Assert.Empty(MediaScanner.ScanReferenced(null));
    }

    [Fact]
    public void Referenced_missing_xvi_is_flagged_missing()
    {
        // A scrap whose -sketch points at an .xvi that doesn't exist → one "Missing" item.
        using var dir = new TempDir();
        var ws = BuildWorkspace(dir);

        var item = Assert.Single(MediaScanner.ScanReferenced(ws));
        Assert.Equal(MediaStatus.Missing, item.Status);
        Assert.Equal("bg.xvi", item.FileName);
        Assert.Equal(1, item.ReferencingScraps);
    }

    [Fact]
    public void Referenced_present_xvi_is_flagged_referenced()
    {
        using var dir = new TempDir();
        var xviPath = dir.Write("bg.xvi", "set XVIgrids {1.0 m}\n");
        var xvi = new XviParser().Parse(xviPath, File.ReadAllText(xviPath)).Value!;
        var ws = BuildWorkspace(dir, xvi);

        var item = Assert.Single(MediaScanner.ScanReferenced(ws));
        Assert.Equal(MediaStatus.Referenced, item.Status);
        Assert.Equal("bg.xvi", item.FileName);
        Assert.Equal(1, item.ReferencingScraps);
    }

    [Fact]
    public void Referenced_xvi_on_disk_but_not_parsed_is_flagged_referenced()
    {
        using var dir = new TempDir();
        dir.Write("bg.xvi", "set XVIgrids {1.0 m}\n");
        var ws = BuildWorkspace(dir);

        var item = Assert.Single(MediaScanner.ScanReferenced(ws));
        Assert.Equal(MediaStatus.Referenced, item.Status);
    }

    private static WorkspaceSemanticModel BuildWorkspace(TempDir dir, params XviFile[] xvi)
    {
        var th2 = dir.Write("plan.th2",
            "scrap s1 -projection plan -sketch \"bg.xvi\" 0 0\n" +
            "endscrap\n");
        var parsed = new Dictionary<string, ParseResult<TherionFile>> { [th2] = new Th2Parser().Parse(th2, File.ReadAllText(th2)) };
        return WorkspaceSemanticModel.Build(parsed, xvi);
    }

    [Fact]
    public void Media_item_exposes_display_helpers()
    {
        var m = new MediaItem("/a/b/scan.xvi", "xvi", MediaStatus.Missing, "10×10 grid", 2, true);
        Assert.Equal("scan.xvi", m.FileName);
        Assert.Equal("Missing", m.StatusText);
        Assert.Equal("yes", m.Georef);
    }
}
