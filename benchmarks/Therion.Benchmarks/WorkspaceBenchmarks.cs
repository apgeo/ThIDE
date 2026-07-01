// Group G/H baseline — the cost the workspace pays today on EVERY change:
// WorkspaceSemanticModel.Build re-binds every file and rebuilds every FrozenDictionary
// from scratch. "RebuildAfterSingleEdit" re-parses one file then rebuilds the whole model,
// which is exactly what TherionWorkspace.OnFileChanged → BuildSemanticModel does now.
// Run:  dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter *Workspace*

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Benchmarks;

[MemoryDiagnoser]
public class WorkspaceBenchmarks
{
    [Params(10, 50)]
    public int Files;

    private const int LegsPerFile = 500;

    private (string Path, string Text)[] _sources = System.Array.Empty<(string, string)>();
    private Dictionary<string, ParseResult<TherionFile>> _parsed = new();

    [GlobalSetup]
    public void Setup()
    {
        _sources = SyntheticData.Project(Files, LegsPerFile);
        _parsed = new Dictionary<string, ParseResult<TherionFile>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in _sources)
            _parsed[path] = new ThParser().Parse(path, text);
    }

    /// <summary>Full workspace semantic build from already-parsed files (cross-file indexes included).</summary>
    [Benchmark(Baseline = true)]
    public int BuildWorkspaceModel()
    {
        var model = WorkspaceSemanticModel.Build(_parsed, System.Array.Empty<XviFile>());
        return model.PerFile.Count;
    }

    /// <summary>
    /// Re-parse a single edited file, then rebuild the whole workspace model — today's
    /// per-edit cost. Group G should make this independent of <see cref="Files"/>.
    /// </summary>
    [Benchmark]
    public int RebuildAfterSingleEdit()
    {
        var (path, text) = _sources[0];
        _parsed[path] = new ThParser().Parse(path, text);
        var model = WorkspaceSemanticModel.Build(_parsed, System.Array.Empty<XviFile>());
        return model.PerFile.Count;
    }
}
