using System.Diagnostics;
using ModelContextProtocol.Client;

namespace Therion.Mcp.Evals;

/// <param name="ServerDll">Path to <c>therion-mcp.dll</c> (run via <c>dotnet</c>), spawned per case over stdio.</param>
/// <param name="WorkspacesDir">Directory holding the committed fixture workspaces.</param>
public sealed record RunConfig(
    string Endpoint, string Model, string? ApiKey, string ServerDll, string WorkspacesDir, int MaxTurns, string? Filter);

/// <summary>
/// Runs the suite: for each case it takes a fresh working copy of the fixture (so a mutation never dirties
/// the committed workspace), spawns the <c>full</c>-profile server over stdio on that copy, drives the model
/// through the tool loop, and grades the end state — deterministically. One command, one scorecard.
/// </summary>
public sealed class EvalRunner(RunConfig config, OpenAiClient model)
{
    public async Task<IReadOnlyList<CaseRun>> RunAsync(Action<CaseRun> onCase, CancellationToken ct)
    {
        var runs = new List<CaseRun>();
        foreach (var evalCase in EvalSuite.Cases)
        {
            if (config.Filter is { } f && !evalCase.Id.Contains(f, StringComparison.OrdinalIgnoreCase)) continue;

            var run = await RunCaseAsync(evalCase, ct);
            runs.Add(run);
            onCase(run);
        }
        return runs;
    }

    private async Task<CaseRun> RunCaseAsync(EvalCase evalCase, CancellationToken ct)
    {
        var workingCopy = CopyFixture(evalCase.Workspace);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var client = await ConnectAsync(workingCopy, ct);
            var tools = (await client.ListToolsAsync(cancellationToken: ct)).ToList();

            var conversation = await model.RunAsync(client, tools, evalCase.Prompt, config.MaxTurns, ct);
            var input = new GradeInput(client, conversation.FinalText, conversation.Calls, workingCopy);
            var (passed, detail) = await Grader.GradeAsync(evalCase.Check, input, ct);

            return new CaseRun(evalCase, conversation.FinalText, conversation.Calls,
                conversation.Turns, conversation.Tokens, stopwatch.Elapsed, passed, detail);
        }
        catch (Exception ex)
        {
            return new CaseRun(evalCase, $"(harness error) {ex.Message}", [], 0, 0, stopwatch.Elapsed, false, ex.Message);
        }
        finally
        {
            TryDelete(workingCopy);
        }
    }

    private async Task<McpClient> ConnectAsync(string workspaceDir, CancellationToken ct)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "therion-mcp (eval)",
            Command = "dotnet",
            Arguments = [config.ServerDll, "--workspace", workspaceDir, "--profile", "full"],
            StandardErrorLines = _ => { },   // the server logs to stderr; keep it out of the eval output
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private string CopyFixture(string name)
    {
        var source = Path.Combine(config.WorkspacesDir, name);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"No fixture workspace '{name}' under {config.WorkspacesDir}.");

        var destination = Path.Combine(Path.GetTempPath(), "therion-eval", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return destination;
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best effort — a temp dir */ }
    }
}
