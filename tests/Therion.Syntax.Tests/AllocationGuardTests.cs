// Phase 0 performance baseline — allocation-regression guards (docs/perf-optimization-plan.md).
//
// These lock in the *managed allocation* of the lexer + parser so the Phase 1/2 optimization
// groups (tokenizer trivia, lazy token text, keyword dispatch, …) can be proven to lower it and
// so nothing later regresses it. Budgets are deliberately generous ceilings; ratchet them DOWN as
// each group lands. The measured baseline at the time of writing is recorded next to each budget.

using System;
using System.Text;
using Therion.Syntax;
using Xunit.Abstractions;

namespace Therion.Syntax.Tests;

public class AllocationGuardTests
{
    private readonly ITestOutputHelper _out;
    public AllocationGuardTests(ITestOutputHelper output) => _out = output;

    private static string Centreline(int legs)
    {
        var sb = new StringBuilder(legs * 32);
        sb.Append("survey big\n  centreline\n    data normal from to length compass clino\n");
        for (int i = 0; i < legs; i++)
            sb.Append("      s").Append(i).Append(" s").Append(i + 1)
              .Append(" 10.0 ").Append(i % 360).Append(' ').Append((i % 179) - 89).Append('\n');
        sb.Append("  endcentreline\nendsurvey\n");
        return sb.ToString();
    }

    /// <summary>Allocates, measuring the steady-state delta after warming up JIT + static ctors.</summary>
    private long Measure(string label, Action action)
    {
        action();
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        _out.WriteLine($"{label}: {delta:N0} bytes ({delta / 1024.0 / 1024.0:F2} MB)");
        return delta;
    }

    [Fact]
    public void Tokenize_5000_legs_allocation_within_budget()
    {
        var text = Centreline(5_000);
        var tokenizer = new TherionTokenizer();
        long bytes = Measure("Tokenize(5000)", () => tokenizer.Tokenize("perf.th", text));

        // BASELINE 2026-07-01 (pre-optimization): 10.04 MB. Ceiling = baseline + ~30%.
        // Ratchet DOWN as Group A (no trivia text / lazy token text) lands.
        const long Budget = 14L * 1024 * 1024;
        Assert.True(bytes < Budget, $"Tokenize(5000) allocated {bytes:N0} bytes, budget {Budget:N0}.");
    }

    [Fact]
    public void Parse_5000_legs_allocation_within_budget()
    {
        var text = Centreline(5_000);
        long bytes = Measure("Parse(5000)", () => new ThParser().Parse("perf.th", text));

        // BASELINE 2026-07-01 (pre-optimization): 18.47 MB. Ceiling = baseline + ~30%.
        // Ratchet DOWN as Groups A/B/C (tokenizer, logical-line slices, keyword dispatch) land.
        const long Budget = 25L * 1024 * 1024;
        Assert.True(bytes < Budget, $"Parse(5000) allocated {bytes:N0} bytes, budget {Budget:N0}.");
    }
}
