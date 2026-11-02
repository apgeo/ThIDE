using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Therion.Mcp.Evals;

namespace Therion.Mcp.Tests;

/// <summary>
/// The eval harness's checks graded against runs that did <em>not</em> do the task. A check that
/// passes such a run inflates task_success / qa_exact, and the committed suite is only as honest as
/// its weakest check. Ground truth comes from the real server over stdio, as in the harness.
/// </summary>
public class EvalGraderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    // ---- end-state checks that hold before the model does anything -----------------------------

    [Fact]
    public async Task LintClean_FailsForACaseWhoseWorkspaceIsAlreadyClean_WhenNothingWasDone()
    {
        // Cases graded by "lints clean" on a fixture that already lints clean: a run with no model
        // turn at all must not pass them (the check may be composite: lint plus evidence of the edit).
        var cases = EvalSuite.Cases.Where(c => Flatten(c.Check).Any(k => k is LintClean)
            && c.Workspace is "valid" or "unformatted").ToList();
        Assert.NotEmpty(cases);

        foreach (var evalCase in cases)
        {
            using var copy = FixtureCopy.Of(evalCase.Workspace);
            using var cts = new CancellationTokenSource(Timeout);
            await using var client = await ServerHost.ConnectAsync(cts.Token,
                "--workspace", copy.PathTo("cave.thconfig"), "--profile", "full");

            var untouched = new GradeInput(client, FinalText: "", Calls: [], WorkspaceDir: copy.Root);
            var (passed, detail) = await Grader.GradeAsync(evalCase.Check, untouched, cts.Token);

            Assert.False(passed, $"'{evalCase.Id}' passes although no tool ran and nothing changed ({detail}).");
        }
    }

    [Fact]
    public async Task HandledGracefully_FailsWhenTheModelCompliedWithTheDestructiveRequest()
    {
        var evalCase = EvalSuite.Cases.Single(c => c.Id == "refuse-delete");
        using var copy = FixtureCopy.Of(evalCase.Workspace);
        // The model did exactly what the refusal case asks it not to do.
        File.WriteAllText(copy.PathTo("main.th"), "");
        var complied = new GradeInput(null!, "Done. main.th is now empty.",
            [new ToolCallRecord("edit_file", SchemaValid: true, Ok: true)], copy.Root);

        var (passed, detail) = await Grader.GradeAsync(evalCase.Check, complied, CancellationToken.None);

        Assert.False(passed, $"'{evalCase.Id}' passes a run that emptied the file ({detail}).");
    }

    [Fact]
    public async Task FileContains_FailsAnExportLineWithAFormatTheCompilerRejects()
    {
        var evalCase = EvalSuite.Cases.Single(c => c.Id == "author-export-model");
        using var copy = FixtureCopy.Of(evalCase.Workspace);
        // "lox" is the extension, "loch" the format keyword; `-fmt lox` is the guess therion rejects.
        File.AppendAllText(copy.PathTo("cave.thconfig"), "\nexport model -fmt lox -o cave.lox\n");
        var wrongFormat = new GradeInput(null!, "Added the export line.",
            [new ToolCallRecord("edit_file", SchemaValid: true, Ok: true)], copy.Root);

        var (passed, detail) = await Grader.GradeAsync(evalCase.Check, wrongFormat, CancellationToken.None);

        Assert.False(passed, $"'{evalCase.Id}' accepts an export line therion would reject ({detail}).");
    }

    // ---- answer matching ----------------------------------------------------------------------

    [Fact]
    public async Task AnswerMatchesComputed_RejectsANumberThatMerelyContainsTheExpectedDigits()
    {
        var evalCase = EvalSuite.Cases.Single(c => c.Id == "qa-legs");
        using var copy = FixtureCopy.Of(evalCase.Workspace);
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token,
            "--workspace", copy.PathTo("cave.thconfig"), "--profile", "full");
        var check = Assert.IsType<AnswerMatchesComputed>(evalCase.Check);
        var expected = await ComputeAsync(client, check, cts.Token);
        var wrongAnswer = "1" + expected;   // 3 → "13", 50 → "150": a different number with the same digits inside

        var (passed, detail) = await Grader.GradeAsync(check, new GradeInput(client, wrongAnswer, [], copy.Root), cts.Token);

        Assert.False(passed, $"'{evalCase.Id}' accepts the answer '{wrongAnswer}' for the computed value {expected} ({detail}).");
    }

    [Fact]
    public async Task AnswerContains_RejectsANegatedAnswer()
    {
        var evalCase = EvalSuite.Cases.Single(c => c.Id == "orient-broken");
        var negated = new GradeInput(null!, "The project is valid: get_diagnostics reports no errors.", [], "");

        var (passed, detail) = await Grader.GradeAsync(evalCase.Check, negated, CancellationToken.None);

        Assert.False(passed, $"'{evalCase.Id}' accepts an answer that says the project has no errors ({detail}).");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static IEnumerable<Check> Flatten(Check check) =>
        check is AllOf all ? all.Checks.SelectMany(Flatten) : [check];

    /// <summary>The ground truth the way the grader derives it: call the tool, read <c>data</c> at the pointer.</summary>
    private static async Task<string> ComputeAsync(McpClient client, AnswerMatchesComputed check, CancellationToken ct)
    {
        var args = (check.Args ?? new Dictionary<string, object?>())
            .Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);
        var result = await client.CallToolAsync(check.Tool, args, cancellationToken: ct);
        var json = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
        foreach (var segment in check.Pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
            element = element.GetProperty(segment);
        return element.GetRawText();
    }

    /// <summary>A throwaway copy of a committed eval workspace, found next to the test binaries
    /// (the harness project copies <c>workspaces/**</c> to its output, which flows to ours).</summary>
    private sealed class FixtureCopy : IDisposable
    {
        public string Root { get; }

        private FixtureCopy(string root) => Root = root;

        public static FixtureCopy Of(string name)
        {
            var source = Candidates(name).FirstOrDefault(Directory.Exists);
            Assert.True(source is not null, $"eval workspace '{name}' not found; looked in: {string.Join(", ", Candidates(name))}");

            var root = WorkspacePaths.Canonicalize(Path.Combine(Path.GetTempPath(), "thmcp_eval_" + Guid.NewGuid().ToString("N")));
            foreach (var file in Directory.GetFiles(source!, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(source!, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            return new FixtureCopy(root);
        }

        private static IEnumerable<string> Candidates(string name)
        {
            yield return Path.Combine(AppContext.BaseDirectory, "workspaces", name);
            var evalsDir = Path.GetDirectoryName(typeof(EvalSuite).Assembly.Location);
            if (evalsDir is not null) yield return Path.Combine(evalsDir, "workspaces", name);
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                yield return Path.Combine(dir.FullName, "src", "Therion.Mcp.Evals", "workspaces", name);
        }

        public string PathTo(params string[] segments) => Path.Combine([Root, .. segments]);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort on a temp dir */ }
        }
    }
}
