namespace Therion.Mcp.Evals;

/// <summary>
/// Validates everything about the harness that does not need a model: the suite's integrity (unique ids,
/// every category covered, every fixture present) and the scorecard arithmetic. Runnable in CI —
/// <c>therion-mcp-evals --self-test</c> — so a broken fixture or a scoring regression fails fast, long
/// before a real (expensive, hardware-bound) run.
/// </summary>
public static class SelfTest
{
    public static (bool Ok, IReadOnlyList<string> Lines) Run(string workspacesDir)
    {
        var lines = new List<string>();
        bool ok = true;
        void Check(string name, bool pass, string? detail = null)
        {
            ok &= pass;
            lines.Add($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail is null ? "" : $" — {detail}")}");
        }

        // ---- suite integrity ----
        var ids = EvalSuite.Cases.Select(c => c.Id).ToList();
        Check("case ids are unique", ids.Distinct().Count() == ids.Count);

        var missingCat = Enum.GetValues<Category>().Where(c => EvalSuite.Cases.All(x => x.Category != c)).ToList();
        Check("every category has a case", missingCat.Count == 0,
            missingCat.Count == 0 ? null : "missing: " + string.Join(", ", missingCat));

        var missingWs = EvalSuite.Cases.Select(c => c.Workspace).Distinct()
            .Where(w => !Directory.Exists(Path.Combine(workspacesDir, w))).ToList();
        Check("every fixture workspace exists", missingWs.Count == 0,
            missingWs.Count == 0 ? $"{EvalSuite.Cases.Count} cases" : "missing: " + string.Join(", ", missingWs));

        // ---- scorecard arithmetic (canned runs) ----
        var runs = new[]
        {
            Fake(Category.Qa, passed: true, turns: 1, tokens: 100, wallMs: 1000, valid: 2, invalid: 0),
            Fake(Category.Repair, passed: true, turns: 2, tokens: 200, wallMs: 2000, valid: 1, invalid: 0),
            Fake(Category.Orientation, passed: false, turns: 1, tokens: 300, wallMs: 3000, valid: 1, invalid: 1),
        };
        var card = Scorecard.Compute(runs);

        Check("call_validity = 4/5", Near(card.CallValidity, 0.80));
        Check("task_success = 2/3", Near(card.TaskSuccess, 2.0 / 3));
        Check("repair@3 = 1/1", Near(card.RepairAt3, 1.0));
        Check("qa_exact = 1/1", Near(card.QaExact, 1.0));
        Check("median tokens = 200", card.MedianTokens == 200, card.MedianTokens.ToString());
        Check("median wall = 2000ms", card.MedianWallMs == 2000, card.MedianWallMs + "ms");

        // A repair that overran the turn budget must NOT count.
        var overran = new[] { Fake(Category.Repair, passed: true, turns: 5, tokens: 0, wallMs: 10, valid: 1, invalid: 0) };
        Check("repair@3 excludes over-budget repairs", Near(Scorecard.Compute(overran).RepairAt3, 0.0));

        // An all-empty run divides by zero cleanly (n/a, not a crash).
        var emptyCard = Scorecard.Compute([]);
        Check("empty run renders n/a", emptyCard.CallValidity is null && emptyCard.RepairAt3 is null);

        // ---- JSON pointer (Grader): array indices are what make per-item ground truth computable ----
        using (var doc = System.Text.Json.JsonDocument.Parse(
            """{"total":2,"surveys":[{"name":"a","length":40},{"name":"b","length":50}]}"""))
        {
            var root = doc.RootElement;
            static string? Text(System.Text.Json.JsonElement? e) => e?.GetRawText();

            Check("pointer: object property", Text(Grader.Resolve(root, "/total")) == "2");
            Check("pointer: array index into object", Text(Grader.Resolve(root, "/surveys/1/length")) == "50");
            Check("pointer: index past the end resolves to nothing",
                Grader.Resolve(root, "/surveys/9/length") is null);
            Check("pointer: non-numeric segment on an array resolves to nothing",
                Grader.Resolve(root, "/surveys/first/length") is null);
            // A negative index must not throw or wrap — "-1" is not an RFC 6901 index.
            Check("pointer: negative index resolves to nothing", Grader.Resolve(root, "/surveys/-1") is null);
            Check("pointer: unknown property resolves to nothing", Grader.Resolve(root, "/nope") is null);
        }

        // ---- schema-token estimator (CAP-02.3): deterministic ~4 chars/token ----
        Check("token estimate: empty is 0", TokenEstimator.Estimate("") == 0 && TokenEstimator.Estimate(null) == 0);
        Check("token estimate: ~4 chars/token, rounded up", TokenEstimator.Estimate("12345678") == 2 && TokenEstimator.Estimate("123") == 1);
        Check("token estimate: tool = name + description + schema",
            TokenEstimator.EstimateTool("abcd", "abcd", "abcd") == 3);

        return (ok, lines);
    }

    private static bool Near(double? actual, double expected) => actual is { } a && Math.Abs(a - expected) < 1e-6;

    private static CaseRun Fake(Category category, bool passed, int turns, int tokens, long wallMs, int valid, int invalid)
    {
        var calls = Enumerable.Repeat(new ToolCallRecord("t", true, true), valid)
            .Concat(Enumerable.Repeat(new ToolCallRecord("nope", false, false), invalid)).ToList();
        var probe = new EvalCase("probe", category, "valid", "p", new HandledGracefully());
        return new CaseRun(probe, "text", calls, turns, tokens, TimeSpan.FromMilliseconds(wallMs), passed, "");
    }
}
