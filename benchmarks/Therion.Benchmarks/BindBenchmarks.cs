// Group D/E baselines — semantic binding (QualifiedName hashing, per-value validation).
// Run:  dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter *Bind*

using BenchmarkDotNet.Attributes;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Benchmarks;

[MemoryDiagnoser]
public class BindBenchmarks
{
    [Params(5_000, 20_000)]
    public int Legs;

    private TherionFile _parsed = null!;

    [GlobalSetup]
    public void Setup()
    {
        var src = SyntheticData.Centreline(Legs);
        _parsed = new ThParser().Parse("bench.th", src).Value!;
    }

    /// <summary>Bind only — the parse tree is prepared once in setup.</summary>
    [Benchmark]
    public int Bind()
    {
        var model = new SemanticBinder().Bind(_parsed);
        return model.Shots.Length;
    }
}

[MemoryDiagnoser]
public class ParseAndBindBenchmarks
{
    [Params(5_000, 20_000)]
    public int Legs;

    private string _source = string.Empty;

    [GlobalSetup]
    public void Setup() => _source = SyntheticData.Centreline(Legs);

    /// <summary>End-to-end parse + bind — the cold-open / CLI path.</summary>
    [Benchmark]
    public int ParseAndBind()
    {
        var parsed = new ThParser().Parse("bench.th", _source).Value!;
        var model = new SemanticBinder().Bind(parsed);
        return model.Stations.Count;
    }
}
