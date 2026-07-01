// Group I/J baselines — connectivity queries (BFS per call today) and go-to-definition
// (QualifiedName.Parse per call + per-file walk today).
// Run:  dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter *Connectivity* *Navigation*

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Benchmarks;

[MemoryDiagnoser]
public class ConnectivityBenchmarks
{
    [Params(5_000)]
    public int Legs;

    private SemanticModel _model = SemanticModel.Empty;
    private ConnectivityGraph _graph = null!;
    private QualifiedName _near, _far;

    [GlobalSetup]
    public void Setup()
    {
        var parsed = new ThParser().Parse("bench.th", SyntheticData.Centreline(Legs)).Value!;
        _model = new SemanticBinder().Bind(parsed);
        _graph = ConnectivityGraph.Build(_model);
        _near = QualifiedName.Parse("big.s0");
        _far = QualifiedName.Parse($"big.s{Legs}");
    }

    /// <summary>Rebuild the connectivity graph (components, dead-ends, …) from the bound model.</summary>
    [Benchmark]
    public int BuildGraph() => ConnectivityGraph.Build(_model).NodeCount;

    /// <summary>Reachability query across the full skeleton — a fresh BFS per call today.</summary>
    [Benchmark]
    public bool AreConnected_EndToEnd() => _graph.AreConnected(_near, _far);
}

[MemoryDiagnoser]
public class NavigationBenchmarks
{
    [Params(20_000)]
    public int Legs;

    private WorkspaceSemanticModel _workspace = WorkspaceSemanticModel.Empty;
    private WorkspaceSymbolNavigationService _nav = null!;
    private string _target = "big.s0";

    [GlobalSetup]
    public void Setup()
    {
        const string path = "bench.th";
        var parsed = new ThParser().Parse(path, SyntheticData.Centreline(Legs));
        var files = new Dictionary<string, ParseResult<TherionFile>>(System.StringComparer.OrdinalIgnoreCase)
        {
            [path] = parsed,
        };
        _workspace = WorkspaceSemanticModel.Build(files, System.Array.Empty<XviFile>());
        _nav = new WorkspaceSymbolNavigationService(_workspace);
        _target = $"big.s{Legs / 2}";
    }

    /// <summary>Resolve a deep station by qualified name (parse + per-file walk today).</summary>
    [Benchmark]
    public SourceSpan? GoToDefinition() => _nav.GoToDefinition(_target);
}
