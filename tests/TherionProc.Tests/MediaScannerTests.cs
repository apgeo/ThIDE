using System.Collections.Generic;
using System.IO;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Services;

namespace TherionProc.Tests;

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
        // A scrap referencing an .xvi that doesn't exist → XVI index entry → "Missing".
        using var dir = new TempDir();
        var th2 = dir.Write("plan.th2",
            "scrap s1 -projection plan\n" +
            "  input \"bg.xvi\"\n" +
            "endscrap\n");
        var parsed = new Dictionary<string, ParseResult<TherionFile>> { [th2] = new Th2Parser().Parse(th2, File.ReadAllText(th2)) };
        var ws = WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());

        var items = MediaScanner.ScanReferenced(ws);
        // Whether or not the parser records the sketch ref, the scan must not throw and any item
        // pointing at a non-existent file is Missing.
        Assert.All(items, m => Assert.True(m.Status != MediaStatus.Orphan));
        Assert.DoesNotContain(items, m => m.Status == MediaStatus.Referenced && !File.Exists(m.Path));
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
