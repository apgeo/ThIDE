// therion-mcp-evals: the deterministic eval harness (T-05.2). Drives a local model (any OpenAI-compatible
// endpoint — LM Studio, Ollama) through the MCP tool loop over a committed prompt set, and scores the end
// state with the library's own answers (D-011: no LLM judge). One command → one MODEL-EVALS row.
// `--self-test` validates the suite + scoring with no model. See README.md.

using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Therion.Mcp.Evals;

if (args is ["-h"] or ["--help"] or [])
{
    PrintHelp();
    return args is [] ? 1 : 0;
}

var workspacesDir = Opt("--workspaces") ?? Path.Combine(AppContext.BaseDirectory, "workspaces");

if (args.Contains("--self-test"))
{
    var (ok, lines) = SelfTest.Run(workspacesDir);
    Console.WriteLine("Self-test:");
    foreach (var line in lines) Console.WriteLine(line);
    Console.WriteLine(ok ? "OK" : "FAILED");
    return ok ? 0 : 1;
}

var serverDll = Opt("--server") ?? ServerDllFromMetadata();
if (serverDll is null || !File.Exists(serverDll))
{
    Console.Error.WriteLine($"error: therion-mcp.dll not found ({serverDll ?? "unset"}). Build the solution, or pass --server <path>.");
    return 2;
}

if (args.Contains("--probe"))
{
    Console.WriteLine("Probing fixtures (ground truth the graders will compute; no model)…");
    var probeOk = await Prober.RunAsync(serverDll, workspacesDir, CancellationToken.None);
    Console.WriteLine(probeOk ? "OK" : "FAILED");
    return probeOk ? 0 : 1;
}

var modelName = Opt("--model");
if (modelName is null)
{
    Console.Error.WriteLine("error: --model is required (or use --self-test).");
    return 2;
}

var endpoint = Opt("--endpoint") ?? "http://localhost:1234/v1";
var apiKey = Opt("--api-key");
int maxTurns = int.TryParse(Opt("--max-turns"), out var mt) ? mt : 12;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
if (apiKey is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

var profile = Opt("--profile") ?? "full";
var config = new RunConfig(endpoint, modelName, apiKey, serverDll, workspacesDir, maxTurns, Opt("--filter"), profile);
var runner = new EvalRunner(config, new OpenAiClient(http, endpoint, modelName));

Console.WriteLine($"Evaluating {modelName} @ {endpoint} — spawning therion-mcp per case (full profile)…\n");
var runs = await runner.RunAsync(
    run => Console.WriteLine($"  [{(run.Passed ? "pass" : "FAIL")}] {run.Case.Id,-22} {run.Case.Category,-12} {run.Detail}"),
    CancellationToken.None);

var scorecard = Scorecard.Compute(runs);
var row = scorecard.ToMarkdownRow(Opt("--run-id") ?? "R-???", modelName, Opt("--notes") ?? "");

Console.WriteLine($"\nScorecard ({runs.Count(r => r.Passed)}/{runs.Count} passed):");
Console.WriteLine(scorecard.ToConsole());
Console.WriteLine("\nMODEL-EVALS row (host ①):");
Console.WriteLine(row);

if (Opt("--out") is { } outPath) WriteOutputs(outPath, runs, row);
return 0;

// ---- helpers ------------------------------------------------------------------------------------

string? Opt(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string? ServerDllFromMetadata() =>
    Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "TherionMcpServerDll")?.Value;

static void WriteOutputs(string path, IReadOnlyList<CaseRun> runs, string row)
{
    File.WriteAllText(Path.ChangeExtension(path, ".md"), row + "\n");
    var detail = runs.Select(r => new
    {
        id = r.Case.Id, category = r.Case.Category.ToString(), passed = r.Passed, turns = r.Turns,
        tokens = r.Tokens, wallMs = (long)r.Wall.TotalMilliseconds, detail = r.Detail,
        calls = r.Calls.Select(c => new { c.Tool, c.SchemaValid, c.Ok }),
        finalText = r.FinalText,
    });
    File.WriteAllText(Path.ChangeExtension(path, ".json"),
        JsonSerializer.Serialize(detail, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"\nWrote {Path.ChangeExtension(path, ".md")} and {Path.ChangeExtension(path, ".json")}.");
}

static void PrintHelp()
{
    Console.WriteLine("therion-mcp-evals — deterministic MCP eval harness (MODEL-EVALS)");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  therion-mcp-evals --model <name> [--endpoint <url>] [options]");
    Console.WriteLine("  therion-mcp-evals --self-test");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --model <name>       Model id the endpoint expects (required).");
    Console.WriteLine("  --endpoint <url>     OpenAI-compatible base URL (default http://localhost:1234/v1).");
    Console.WriteLine("  --api-key <key>      Bearer token, if the endpoint needs one.");
    Console.WriteLine("  --server <path>      therion-mcp.dll to spawn (default: the built one).");
    Console.WriteLine("  --workspaces <dir>   Fixture workspaces dir (default: ./workspaces beside the exe).");
    Console.WriteLine("  --profile <p>        Server profile the model sees: data (21 read-only) or full (default, 33).");
    Console.WriteLine("  --max-turns <n>      Tool-loop turn budget per case (default 12).");
    Console.WriteLine("  --filter <substr>    Run only cases whose id contains this.");
    Console.WriteLine("  --run-id <id>        Label for the MODEL-EVALS row (e.g. R-001).");
    Console.WriteLine("  --notes <text>       Notes column for the row.");
    Console.WriteLine("  --out <path>         Write <path>.md (the row) and <path>.json (per-case detail).");
    Console.WriteLine("  --self-test          Validate the suite + scoring without a model, then exit.");
    Console.WriteLine("  --probe              Spawn the server on each fixture and print the ground truth, then exit.");
}
