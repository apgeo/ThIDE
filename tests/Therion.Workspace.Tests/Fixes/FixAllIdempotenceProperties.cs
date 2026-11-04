// Fix all over generated caves with known defects: a removed block terminator, an include path written
// with '\' (off Windows), and noise no fix may touch (an upper-case keyword). After one
// fix-all per batch code the files are the clean cave again plus the noise, nothing fixable is left,
// a second fix-all plans nothing, and a diagnostic listed twice is fixed once.
// Seeds are fixed; THIDE_PROPERTY_RUNS raises the case count. A failure names its seed.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Semantics.Tests.Properties.CaveGen;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;
using Therion.Workspace.Formatting;

namespace Therion.Workspace.Tests.Fixes;

public class FixAllIdempotenceProperties
{
    private const int FirstSeed = 29_000;

    private static readonly PrintFeatures Features = PrintFeatures.None with { SplitFiles = true, SplitCentrelines = true, Units = true };

    [Fact]
    public void Fix_all_repairs_the_defects_once_and_touches_nothing_else()
    {
        int terminators = 0, separators = 0;
        foreach (var seed in Enumerable.Range(FirstSeed, PropertyRuns.Count(150)))
        {
            var (problem, t, s) = Check(seed);
            if (problem is not null) Assert.Fail($"[seed {seed}] {problem}");
            terminators += t;
            separators += s;
        }
        Assert.True(terminators >= 30, $"only {terminators} terminators were removed and restored");
        if (!OperatingSystem.IsWindows()) Assert.True(separators >= 20, $"only {separators} include paths were rewritten");
    }

    private static (string? Problem, int Terminators, int Separators) Check(int seed)
    {
        var rnd = new SeededRandom(seed * 13 + 5);
        var cave = CaveGenerator.Generate(seed);
        // Indented the way Format Document leaves it: a terminator fix is batch-safe only under an indented body.
        var printed = CavePrinter.Print(CaveShrinker.Clone(cave), Features, seed)
            .Select(f => f with { Text = TherionReindenter.Format(f.Text, "  ") }).ToList();
        if (BinderOracle.Check(cave, printed) is { Count: > 0 } before) return ("the clean cave is not recovered: " + before[0], 0, 0);

        // ---- defects, and the text fix-all must give back ----
        var damaged = printed.ToDictionary(f => f.Path, f => f.Text);
        var expected = printed.ToDictionary(f => f.Path, f => f.Text);
        int terminators = 0, separators = 0;
        var unfixed = new Dictionary<string, string>();   // the clean text still without the removed terminator

        // An include path with '\': a file without inputs of its own moves to a sub-folder.
        if (!OperatingSystem.IsWindows())
        {
            var leaves = printed.Skip(1).Where(f => !f.Text.Contains("input ")).ToList();
            if (leaves.Count > 0 && rnd.Chance(0.7))
            {
                var leaf = rnd.Pick(leaves);
                var name = Path.GetFileName(leaf.Path);
                var moved = Path.Combine(CavePrinter.Folder, "sub", name);
                var parent = printed.First(f => f.Text.Contains("input " + name + "\n"));
                damaged[parent.Path] = damaged[parent.Path].Replace("input " + name + "\n", "input sub\\" + name + "\n");
                expected[parent.Path] = expected[parent.Path].Replace("input " + name + "\n", "input sub/" + name + "\n");
                damaged[moved] = expected[moved] = damaged[leaf.Path];
                damaged.Remove(leaf.Path);
                expected.Remove(leaf.Path);
                separators++;
            }
        }

        foreach (var path in damaged.Keys.ToList())
        {
            var lines = damaged[path].Split('\n').ToList();
            // Noise first, in both copies: an upper-case keyword.
            int keyword = lines.FindIndex(l => l.TrimStart() == "centreline");
            if (keyword >= 0 && rnd.Chance(0.4))
            {
                lines[keyword] = lines[keyword].Replace("centreline", "CENTRELINE");
                var clean = expected[path].Split('\n');
                clean[keyword] = lines[keyword];
                expected[path] = string.Join('\n', clean);
            }
            // A removed terminator: the body before it is indented, so the fix knows where it goes.
            var enders = lines.Select((l, i) => (l, i)).Where(x => x.l.TrimStart() is "endsurvey" or "endcentreline").Select(x => x.i).ToList();
            if (enders.Count > 0 && rnd.Chance(0.6))
            {
                int at = rnd.Pick(enders);
                lines.RemoveAt(at);
                var without = expected[path].Split('\n').ToList();
                without.RemoveAt(at);
                unfixed[path] = string.Join('\n', without);
            }
            damaged[path] = string.Join('\n', lines);
        }

        // ---- one fix-all per code, in the order the codes are listed ----
        var files = damaged;
        foreach (var code in CodeFixes.BatchCodes.Order(StringComparer.Ordinal))
        {
            var diagnostics = Diagnose(files);
            var (batch, _, _) = FixAll.Plan(code, diagnostics, files.GetValueOrDefault);
            // The same diagnostic listed twice is fixed once: the second fix overlaps the first.
            var (twice, _, _) = FixAll.Plan(code, diagnostics.Concat(diagnostics), files.GetValueOrDefault);
            if (!SameEdits(batch, twice)) return ($"{code}: a diagnostic listed twice gives other edits", 0, 0);
            files = Apply(files, batch);
        }

        // A removed terminator comes back, or, where fix-all cannot tell which block it closed, it stays
        // out and the rest of the file is clean: a closer of an outer block that follows reads as a mismatched closer
        // (TH0021, not a batch code), and any later closer of the same kind counts as the block's own.
        foreach (var (path, text) in expected)
        {
            if (unfixed.TryGetValue(path, out var still) && files.GetValueOrDefault(path) == still) continue;
            if (unfixed.ContainsKey(path)) terminators++;
            if (files.GetValueOrDefault(path) != text)
                return ($"{Path.GetFileName(path)} after fix-all differs from the clean file:\n--- expected\n{text}\n--- got\n{files.GetValueOrDefault(path)}", 0, 0);
        }

        // ---- nothing fixable is left, and a second run plans nothing ----
        var left = Diagnose(files);
        foreach (var d in left.Where(d => CodeFixes.BatchCodes.Contains(d.Code.Value)))
            if (files.TryGetValue(d.Span.FilePath ?? "", out var t) && CodeFixes.ForDiagnostic(t, d) is not null)
                return ($"a fixable {d.Code} is left at {d.Span}: {d.Message}", 0, 0);
        foreach (var code in CodeFixes.BatchCodes)
            if (!FixAll.Plan(code, left, files.GetValueOrDefault).Batch.IsEmpty)
                return ($"a second fix-all for {code} plans edits", 0, 0);
        return (null, terminators, separators);
    }

    // Each file's parse diagnostics and the project's (include paths checked against the files there are).
    private static List<Diagnostic> Diagnose(IReadOnlyDictionary<string, string> files)
    {
        var parser = new ThParser();
        var parsed = files.ToDictionary(kv => kv.Key, kv => parser.Parse(kv.Key, kv.Value));
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => true);
        var all = parsed.Values.SelectMany(p => p.Diagnostics).ToList();
        all.AddRange(ProjectDiagnostics.Analyze(ws, fileExists: p => files.ContainsKey(Path.GetFullPath(p))));
        return all;
    }

    private static Dictionary<string, string> Apply(Dictionary<string, string> files, EditBatch batch)
    {
        var result = new Dictionary<string, string>(files);
        foreach (var g in batch.Edits)
            result[g.FilePath] = TextSplicer.Splice(result[g.FilePath], g.Edits) ?? throw new InvalidOperationException("edits do not apply to " + g.FilePath);
        return result;
    }

    private static bool SameEdits(EditBatch a, EditBatch b) =>
        a.Edits.Count == b.Edits.Count
        && a.Edits.Zip(b.Edits).All(p => p.First.FilePath == p.Second.FilePath && p.First.Edits.SequenceEqual(p.Second.Edits));

    [Fact]
    public void Nested_unclosed_blocks_are_closed_in_one_run()
    {
        const string text = "survey a\n  centreline\n    1 2 3 0 0\n";
        var diagnostics = new ThParser().Parse("/c/a.th", text).Diagnostics.Where(d => d.Code.Value == CodeFixes.UnterminatedBlock).ToList();

        var (batch, _, _) = FixAll.Plan(CodeFixes.UnterminatedBlock, diagnostics, _ => text);

        Assert.Equal("survey a\n  centreline\n    1 2 3 0 0\n  endcentreline\nendsurvey\n", TextSplicer.Splice(text, batch.Edits.Single().Edits));
    }

    [Fact]
    public void A_diagnostic_listed_twice_inserts_one_terminator()
    {
        const string text = "survey a\n  equate 1 2\n";
        var d = Diagnostic.Create(CodeFixes.UnterminatedBlock, DiagnosticSeverity.Error, "unterminated",
            new SourceSpan("/c/a.th", new SourceLocation(1, 1), new SourceLocation(1, 1), 0, 0));

        var (batch, fixedCount, skipped) = FixAll.Plan(CodeFixes.UnterminatedBlock, [d, d], _ => text);

        Assert.Equal((1, 1), (fixedCount, skipped));
        Assert.Equal("survey a\n  equate 1 2\nendsurvey\n", TextSplicer.Splice(text, batch.Edits.Single().Edits));
    }
}
