// Group A/B/C baselines — lexing + parsing throughput and allocation.
// Run:  dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter *Parse* *Tokenize*

using BenchmarkDotNet.Attributes;
using Therion.Syntax;

namespace Therion.Benchmarks;

[MemoryDiagnoser]
public class TokenizeBenchmarks
{
    [Params(5_000, 20_000)]
    public int Legs;

    private string _source = string.Empty;
    private readonly TherionTokenizer _tokenizer = new();

    [GlobalSetup]
    public void Setup() => _source = SyntheticData.Centreline(Legs);

    [Benchmark]
    public int Tokenize()
    {
        var tokens = _tokenizer.Tokenize("bench.th", _source);
        return tokens.Length;
    }
}

[MemoryDiagnoser]
public class ParseBenchmarks
{
    [Params(5_000, 20_000)]
    public int Legs;

    private string _source = string.Empty;

    [GlobalSetup]
    public void Setup() => _source = SyntheticData.Centreline(Legs);

    [Benchmark]
    public object Parse()
    {
        var result = new ThParser().Parse("bench.th", _source);
        return result.Value!;
    }
}
