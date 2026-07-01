// Group G1 — incremental rebind. Verifies WorkspaceSemanticModel.Build reuses a file's bound
// SemanticModel across rebuilds when its ParseResult is unchanged, and re-binds only re-parsed
// files. This is what keeps a single-file edit from re-binding the whole project.

using System;
using System.Collections.Generic;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class BindCacheTests
{
    private const string Src =
        "survey s\n centreline\n data normal from to length compass clino\n 1 2 10 0 0\n endcentreline\nendsurvey\n";

    private static Dictionary<string, ParseResult<TherionFile>> Files(params (string Path, string Text)[] items)
    {
        var d = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in items) d[path] = new ThParser().Parse(path, text);
        return d;
    }

    [Fact]
    public void Rebuild_with_unchanged_parse_reuses_bound_model()
    {
        var files = Files(("a.th", Src));
        var m1 = WorkspaceSemanticModel.Build(files, Array.Empty<XviFile>());
        var m2 = WorkspaceSemanticModel.Build(files, Array.Empty<XviFile>());

        // Same ParseResult instance => same cached SemanticModel instance.
        Assert.Same(m1.PerFile["a.th"], m2.PerFile["a.th"]);
    }

    [Fact]
    public void Reparsed_file_rebinds_while_unchanged_files_are_reused()
    {
        var files = Files(("a.th", Src), ("b.th", Src.Replace("survey s", "survey t")));
        var m1 = WorkspaceSemanticModel.Build(files, Array.Empty<XviFile>());
        var aModel1 = m1.PerFile["a.th"];
        var bModel1 = m1.PerFile["b.th"];

        // Re-parse only b.th → a new ParseResult instance for it.
        files["b.th"] = new ThParser().Parse("b.th", Src.Replace("survey s", "survey t"));
        var m2 = WorkspaceSemanticModel.Build(files, Array.Empty<XviFile>());

        Assert.Same(aModel1, m2.PerFile["a.th"]);      // unchanged parse → reused
        Assert.NotSame(bModel1, m2.PerFile["b.th"]);   // re-parsed → re-bound
    }
}
