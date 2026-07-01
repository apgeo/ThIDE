// Phase 0 performance baseline — allocation-regression guards (docs/perf-optimization-plan.md).
//
// Locks in the *managed allocation* of binding so Phase 1 groups (QualifiedName hash cache,
// per-column classify cache, QualifyLocal churn) can be proven to lower it. Budgets are generous
// ceilings; ratchet DOWN as each group lands. Measured baseline recorded next to each budget.

using System;
using System.Text;
using Therion.Semantics;
using Therion.Syntax;
using Xunit.Abstractions;

namespace Therion.Semantics.Tests;

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
    public void Bind_20000_legs_allocation_within_budget()
    {
        var parsed = new ThParser().Parse("big.th", Centreline(20_000)).Value!;
        long bytes = Measure("Bind(20000)", () => new SemanticBinder().Bind(parsed));

        // BASELINE 2026-07-01 (pre-optimization): 14.84 MB. Ceiling = baseline + ~30%.
        // Ratchet DOWN as Groups D/E (QualifiedName hash cache, per-column classify cache) land.
        const long Budget = 20L * 1024 * 1024;
        Assert.True(bytes < Budget, $"Bind(20000) allocated {bytes:N0} bytes, budget {Budget:N0}.");
    }

    [Fact]
    public void Parse_and_bind_20000_legs_allocation_within_budget()
    {
        var text = Centreline(20_000);
        long bytes = Measure("ParseAndBind(20000)", () =>
        {
            var parsed = new ThParser().Parse("big.th", text).Value!;
            new SemanticBinder().Bind(parsed);
        });

        // BASELINE 2026-07-01 (pre-optimization): 89.00 MB (parse ~74 MB dominates, bind ~15 MB).
        // Ceiling = baseline + ~30%. Ratchet DOWN as the tokenizer/parser groups land.
        const long Budget = 115L * 1024 * 1024;
        Assert.True(bytes < Budget, $"ParseAndBind(20000) allocated {bytes:N0} bytes, budget {Budget:N0}.");
    }

    [Fact]
    public void Build_connectivity_graph_5000_allocation_within_budget()
    {
        var model = new SemanticBinder().Bind(new ThParser().Parse("big.th", Centreline(5_000)).Value!);
        long bytes = Measure("BuildGraph(5000)", () => ConnectivityGraph.Build(model));

        // Group I: Schwartzian sort key (one ToString/node) cut this from ~13.4 MB to ~2.5 MB;
        // Group H (cheaper QualifiedName.ToString) trimmed it to ~2.2 MB. Ceiling ~= current + ~35%.
        const long Budget = 3L * 1024 * 1024;
        Assert.True(bytes < Budget, $"BuildGraph(5000) allocated {bytes:N0} bytes, budget {Budget:N0}.");
    }

    [Fact]
    public void AreConnected_is_allocation_free()
    {
        var model = new SemanticBinder().Bind(new ThParser().Parse("big.th", Centreline(5_000)).Value!);
        var graph = ConnectivityGraph.Build(model);
        var a = QualifiedName.Parse("big.s0");
        var b = QualifiedName.Parse("big.s5000");

        // Group I: O(1) component-id compare, no per-call BFS/HashSet/Queue. 10k calls must stay
        // essentially allocation-free (was ~315 KB *per call*).
        long bytes = Measure("AreConnected x10000", () =>
        {
            for (int i = 0; i < 10_000; i++) graph.AreConnected(a, b);
        });
        Assert.True(bytes < 64 * 1024, $"AreConnected x10000 allocated {bytes:N0} bytes (expected ~0).");
    }

    [Fact]
    public void Build_workspace_model_index_allocation_within_budget()
    {
        var files = new System.Collections.Generic.Dictionary<string, ParseResult<TherionFile>>(
            StringComparer.OrdinalIgnoreCase);
        for (int f = 0; f < 10; f++)
            files[$"f{f}.th"] = new ThParser().Parse($"f{f}.th", Centreline(500));

        // G1's bind cache reuses per-file models across the warmup calls, so the measured call is
        // dominated by the cross-file index build (Group H cheapened its per-station key ToStrings;
        // the remaining floor is the FrozenDictionary construction that a future G2 would target).
        long bytes = Measure("BuildWorkspaceModel(10x500)",
            () => WorkspaceSemanticModel.Build(files, System.Array.Empty<XviFile>()));

        // Post G1+H baseline ~2.6 MB. Ceiling ~= current + ~35%.
        const long Budget = 4L * 1024 * 1024;
        Assert.True(bytes < Budget, $"BuildWorkspaceModel(10x500) allocated {bytes:N0} bytes, budget {Budget:N0}.");
    }
}
