// Every tool the server registers, called with generated arguments the way a confused or hostile agent
// would send them: arguments missing, of the wrong type, of extreme size and sign, paths with NUL,
// `..`, backslashes, absolute paths into a sibling folder holding a secret, and tool names from the
// catalog itself (found by reflection, so a new tool is covered the day it exists). The contract:
//  - a well-typed call returns the result envelope, and a failure carries one of the ToolErrorCodes;
//  - a badly typed call returns an envelope or a plain error, never an exception that escapes;
//  - the result stays within the size cap;
//  - nothing outside the workspace is read into a result or changed, and the project's config folder
//    (hooks, tasks) is never written.
// The workspace, the sibling folder and the sidecar stores are all temp folders. Seeds are fixed
// (Therion.TestSupport.GeneratedRuns).

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Therion.Mcp;
using Therion.Mcp.Dispatch;
using Therion.Processing.Abstractions;
using Therion.TestSupport;
using Therion.Workspace;

namespace Therion.Mcp.Tests;

public sealed class ToolContractFuzzTests : IDisposable
{
    private const int SeedBase = 27_000;
    private const string Secret = "SECRET-OUTSIDE-MARKER-4471";

    private readonly string _scratch = WorkspacePaths.Canonicalize(Directory.CreateTempSubdirectory("thide-contract-").FullName);
    private readonly string _root;
    private readonly string _outside;
    private readonly string _sidecar;
    private readonly string _thconfig;

    public ToolContractFuzzTests()
    {
        _root = Path.Combine(_scratch, "work");
        _outside = Path.Combine(_scratch, "elsewhere");
        _sidecar = Path.Combine(_scratch, "sidecar");
        Directory.CreateDirectory(Path.Combine(_root, "caves"));
        Directory.CreateDirectory(Path.Combine(_root, ".thide"));
        Directory.CreateDirectory(Path.Combine(_outside, "deep"));
        Directory.CreateDirectory(_sidecar);

        _thconfig = Path.Combine(_root, "project.thconfig");
        File.WriteAllText(_thconfig, "source caves/upper.th\nsource caves/big.th\n");
        File.WriteAllText(Path.Combine(_root, "caves", "upper.th"),
            "survey upper\n  centreline\n    data normal from to length compass clino\n    1 2 10.0 90 0\n    2 3 12.5 100 -5\n  endcentreline\nendsurvey\n");
        // Enough shots that a listing with the largest page size asked for is cut by the cap, not by running out.
        var big = new StringBuilder("survey big\n  centreline\n    data normal from to length compass clino\n");
        for (int i = 1; i <= 2300; i++) big.Append($"    s{i} s{i + 1} {5 + i % 7}.5 {i % 360} {i % 40 - 20}\n");
        big.Append("  endcentreline\nendsurvey\n");
        File.WriteAllText(Path.Combine(_root, "caves", "big.th"), big.ToString());
        // A text file past the byte cap.
        File.WriteAllText(Path.Combine(_root, "caves", "notes.txt"), string.Concat(Enumerable.Range(0, 60_000).Select(i => $"note number {i} about the cave\n")));
        File.WriteAllText(Path.Combine(_root, ".thide", "hooks.json"), "{ \"hooks\": [] }");
        File.WriteAllText(Path.Combine(_root, ".thide", "tasks.json"), "{ \"tasks\": [] }");

        File.WriteAllText(Path.Combine(_outside, "secret.th"), $"survey secret\n  ; {Secret}\nendsurvey\n");
        File.WriteAllText(Path.Combine(_outside, "secret.thconfig"), $"source secret.th ; {Secret}\n");
        File.WriteAllText(Path.Combine(_outside, "deep", "notes.txt"), Secret + "\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- the catalog under test ----------------------------------------------------------------

    private sealed class NoTherion : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) => ValueTask.FromResult<ToolInfo?>(null);
    }

    private ToolDispatcher Dispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(_thconfig));
        // The stores and the compiler locator are ours: the defaults would use the user's profile and a real Therion.
        services.AddSingleton<IProjectMetadataStore>(new ProjectMetadataStore(Path.Combine(_sidecar, "metadata")));
        services.AddSingleton<ILeadStatusStore>(new LeadStatusStore(Path.Combine(_sidecar, "leads")));
        services.AddSingleton<IExternalToolLocator>(new NoTherion());
        services.AddToolServices();
        return new ToolDispatcher(services.BuildServiceProvider());
    }

    private static readonly HashSet<string> KnownCodes = typeof(ToolErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    // Tools that reach outside the workspace by design, and how this test keeps them to temp folders:
    //  - load_workspace changes which folder is the workspace, so every later call would run elsewhere; it has
    //    its own test below with paths from the scratch folder only.
    //  - get_declination reads a WMM.COF; without a cofPath it looks in the user's profile, so one is always given.
    private static readonly HashSet<string> OwnTest = ["load_workspace"];

    // ---- argument generation -------------------------------------------------------------------

    private string[] StringPool() =>
    [
        "", " ", "project.thconfig", "caves/upper.th", "caves/big.th", "caves/notes.txt", "caves", ".", "..", "../", "../elsewhere", "../elsewhere/secret.th",
        "caves/../../elsewhere/secret.th", "caves/./../upper.th", "caves\\upper.th", "..\\elsewhere\\secret.th", "caves//upper.th", "caves/upper.th/",
        Path.Combine(_outside, "secret.th"), Path.Combine(_outside, "secret.thconfig"), Path.Combine(_outside, "deep", "notes.txt"), _outside, _scratch,
        "/etc/passwd", "/", "C:\\Windows\\win.ini", "\\\\server\\share\\x.th", "file:///etc/passwd",
        ".thide/hooks.json", ".thide/tasks.json", ".thide", ".THIDE/hooks.json", "caves/../.thide/hooks.json", "hooks.json",
        "caves/a\0b.th", "\0", "caves/upper.th\0.txt", "CON", "NUL", "caves/upper.th.", "caves/upper.th ", "-", "--help",
        "1", "2", "upper", "upper.1", "big", "s1", "s2", "1@upper", "x@y@z", "nothing", "error", "warning", "TH0010", "TH_SEM_001", "survey", "data", "centreline",
        new string('a', 5000), new string('/', 300), string.Concat(Enumerable.Repeat("../", 200)), "\u65E5\u672C\u8A9E.th", "e\u0301.th", "\uD83D\uDE00.th",
        "{\"a\":1}", "[1,2]", "null", "true", "<script>", "'; DROP TABLE x;--", "%00", "%2e%2e/", "$HOME", "~", "${HOME}/.config",
    ];

    private static readonly long[] IntPool = [0, -1, 1, 2, 3, 50, 200, 2000, 2001, 100_000, int.MaxValue, int.MinValue, int.MaxValue - 1, -2];
    private static readonly double[] NumberPool = [0, -1, 1.5, 90, -90, 180, 360, 1e-9, 1e9, 2026.5, double.MaxValue / 2, -1e300, 45.123456];

    private static IEnumerable<string> Types(JsonElement prop)
    {
        if (prop.TryGetProperty("type", out var t))
        {
            if (t.ValueKind == JsonValueKind.String) yield return t.GetString()!;
            else if (t.ValueKind == JsonValueKind.Array) foreach (var x in t.EnumerateArray()) yield return x.GetString()!;
        }
    }

    private object? WellTyped(Random random, JsonElement prop, string name, string tool)
    {
        if (tool == "get_declination" && name == "cofPath") return Path.Combine(_scratch, "no-such.COF");
        if (prop.TryGetProperty("enum", out var e) && e.ValueKind == JsonValueKind.Array && random.Next(5) != 0)
        {
            var values = e.EnumerateArray().ToList();
            return values.Count > 0 ? values[random.Next(values.Count)].Clone() : null;
        }
        var types = Types(prop).ToList();
        var type = types.Count == 0 ? "string" : types[random.Next(types.Count)];
        switch (type)
        {
            case "string": var pool = StringPool(); return pool[random.Next(pool.Length)];
            case "integer": return IntPool[random.Next(IntPool.Length)];
            case "number": return NumberPool[random.Next(NumberPool.Length)];
            case "boolean": return random.Next(2) == 0;
            case "null": return null;
            case "array":
                var item = prop.TryGetProperty("items", out var items) ? items : default;
                return Enumerable.Range(0, random.Next(0, 4)).Select(_ => item.ValueKind == JsonValueKind.Object ? WellTyped(random, item, name, tool) : StringPool()[random.Next(StringPool().Length)]).ToArray();
            case "object": return new Dictionary<string, object?> { ["x"] = random.Next(3) };
            default: return null;
        }
    }

    private object? WrongTyped(Random random) => random.Next(10) switch
    {
        0 => "text", 1 => 42, 2 => -7.5, 3 => true, 4 => null, 5 => new object[] { 1, "a", null! },
        6 => new Dictionary<string, object?> { ["a"] = new[] { 1, 2 } }, 7 => long.MaxValue, 8 => "1e400", _ => new string('9', 400),
    };

    private (Dictionary<string, object?> Arguments, bool WellTypedCall) Generate(Random random, ToolDescriptor tool)
    {
        var args = new Dictionary<string, object?>();
        bool wellTyped = random.Next(10) < 7;
        var schema = tool.Schema;
        var required = schema.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : [];
        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var p in props.EnumerateObject())
            {
                if (p.Name == "progress") continue;   // filled in by the host, not by the agent: the server leaves it out of the schema it serves
                bool must = required.Contains(p.Name) || (tool.Name == "get_declination" && p.Name == "cofPath");
                if (!must && random.Next(10) < 4) continue;
                args[p.Name] = WellTyped(random, p.Value, p.Name, tool.Name);
            }
        if (!wellTyped && args.Count > 0)
        {
            var key = args.Keys.ElementAt(random.Next(args.Count));
            if (random.Next(3) == 0 && required.Contains(key)) args.Remove(key); else args[key] = WrongTyped(random);
        }
        if (random.Next(15) == 0) args["unexpectedArgument"] = WrongTyped(random);
        return (args, wellTyped);
    }

    // ---- the oracle ----------------------------------------------------------------------------

    private static Dictionary<string, string> Snapshot(string folder) =>
        !Directory.Exists(folder) ? [] : Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(folder, p),
                p => Directory.Exists(p) ? "<dir>" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));

    private static string? Violation(string tool, bool wellTyped, ToolCallOutcome outcome)
    {
        if (!outcome.Found) return "the tool was listed but not found";
        if (outcome.Result.ValueKind == JsonValueKind.Object && outcome.Result.TryGetProperty("ok", out var ok) && ok.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var text = outcome.Result.GetRawText();
            if (Encoding.UTF8.GetByteCount(text) > ToolLimits.HardMaxBytes + ToolLimits.HardMaxBytes / 2)
                return $"the result is {Encoding.UTF8.GetByteCount(text)} bytes, past the cap";
            if (text.Contains(Secret, StringComparison.Ordinal)) return "the result holds text from outside the workspace";
            if (text.Contains("root:x:0:0", StringComparison.Ordinal)) return "the result holds the system password file";
            if (ok.ValueKind == JsonValueKind.False)
            {
                if (!outcome.Result.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object
                    || !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String)
                    return "a failure without an error code";
                if (!KnownCodes.Contains(code.GetString()!)) return $"the error code '{code.GetString()}' is not one of ToolErrorCodes";
            }
            return null;
        }
        // No envelope: the tool (or the binding of its arguments) threw. That is fine for a call the
        // agent got wrong, as a plain error the dispatcher reports; it is not for a call that was well typed.
        if (wellTyped) return $"a well-typed call threw instead of returning an envelope: {outcome.Error}";
        return string.IsNullOrWhiteSpace(outcome.Error) ? "neither an envelope nor an error message" : null;
    }

    // ---- the tests -----------------------------------------------------------------------------

    [Fact]
    public async Task Every_tool_keeps_the_contract_under_generated_arguments()
    {
        var dispatcher = Dispatcher();
        var tools = dispatcher.Tools.Where(t => !OwnTest.Contains(t.Name)).ToList();
        Assert.True(tools.Count > 30, $"only {tools.Count} tools in the catalog");

        var outsideBefore = Snapshot(_outside);
        var configBefore = Snapshot(Path.Combine(_root, ".thide"));
        var failures = new List<string>();
        int calls = GeneratedRuns.Count(tools.Count * 40);
        int succeeded = 0;
        var codes = new HashSet<string>();

        for (int i = 0; i < calls; i++)
        {
            int seed = GeneratedRuns.FirstSeed(SeedBase) + i;
            var random = new Random(seed);
            var tool = tools[i % tools.Count];
            var (arguments, wellTyped) = Generate(random, tool);
            var json = JsonSerializer.SerializeToElement(arguments);

            ToolCallOutcome outcome;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { outcome = await dispatcher.CallAsync(tool.Name, json, cts.Token); }
            catch (Exception ex) { failures.Add($"seed {seed} {tool.Name} {json}: the dispatcher itself threw {ex.GetType().Name}: {ex.Message}"); continue; }

            if (outcome.Ok) succeeded++;
            else if (outcome.Result.ValueKind == JsonValueKind.Object && outcome.Result.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var c)) codes.Add(c.GetString()!);

            if (Violation(tool.Name, wellTyped, outcome) is { } problem)
                failures.Add($"seed {seed} {tool.Name} {(wellTyped ? "well typed" : "badly typed")} {Short(json.GetRawText())}: {problem}");

            // After each call: nothing outside has changed, and the config folder is untouched.
            if (!Snapshot(_outside).SequenceEqual(outsideBefore)) { failures.Add($"seed {seed} {tool.Name} {Short(json.GetRawText())}: the sibling folder changed"); outsideBefore = Snapshot(_outside); }
            if (!Snapshot(Path.Combine(_root, ".thide")).SequenceEqual(configBefore)) { failures.Add($"seed {seed} {tool.Name} {Short(json.GetRawText())}: the config folder changed"); configBefore = Snapshot(Path.Combine(_root, ".thide")); }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {calls} calls broke the contract:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(12)) + (failures.Count > 12 ? $"{Environment.NewLine}… and {failures.Count - 12} more" : ""));
        // The generator reaches into the tools: plenty of calls work, and the failures are of many kinds.
        Assert.True(succeeded >= calls / 20, $"only {succeeded} of {calls} calls succeeded, so the arguments are not getting past the first check");
        Assert.True(codes.Count >= 5, $"the failures have only {codes.Count} different codes: {string.Join(", ", codes)}");
        // Nothing but the workspace and the sidecar stores was created next to them.
        Assert.Equal(["elsewhere", "sidecar", "work"], Directory.GetDirectories(_scratch).Select(Path.GetFileName).OrderBy(x => x).ToArray());
        Assert.Empty(Directory.GetFiles(_scratch));
    }

    private static string Short(string s) => s.Length <= 220 ? s : s[..220] + "…";

    [Fact]
    public async Task A_page_or_byte_budget_past_the_cap_is_cut_to_the_cap()
    {
        var dispatcher = Dispatcher();
        var huge = new Dictionary<string, object?> { ["limit"] = int.MaxValue, ["maxBytes"] = int.MaxValue, ["offset"] = 0 };
        foreach (var tool in dispatcher.Tools.Where(t => t.ReadOnly && t.Name != "load_workspace"))
        {
            var props = tool.Schema.TryGetProperty("properties", out var p) ? p : default;
            var args = huge.Where(kv => props.ValueKind == JsonValueKind.Object && props.TryGetProperty(kv.Key, out _)).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (args.Count == 0) continue;
            if (tool.Schema.TryGetProperty("required", out var req))
                foreach (var name in req.EnumerateArray().Select(x => x.GetString()!))
                    args.TryAdd(name, name switch { "path" => "caves/notes.txt", _ => "upper" });
            if (tool.Name == "get_declination") continue;

            var outcome = await dispatcher.CallAsync(tool.Name, JsonSerializer.SerializeToElement(args));

            if (outcome.Result.ValueKind == JsonValueKind.Object)
                Assert.True(Encoding.UTF8.GetByteCount(outcome.Result.GetRawText()) <= ToolLimits.HardMaxBytes * 3 / 2,
                    $"{tool.Name}: {Encoding.UTF8.GetByteCount(outcome.Result.GetRawText())} bytes for the largest page asked for");
        }
    }

    [Fact]
    public async Task Load_workspace_with_hostile_paths_returns_an_envelope()
    {
        var failures = new List<string>();
        var pool = new[]
        {
            "", " ", "\0", "a\0b", "..", "../", "\\\\server\\share\\x.thconfig", "C:\\nothing\\x.thconfig", "CON", new string('x', 5000),
            Path.Combine(_scratch, "no-such-folder"), Path.Combine(_scratch, "no-such.thconfig"), Path.Combine(_root, "caves", "notes.txt"),
            Path.Combine(_root, "caves", "no-such.th"), "file:///nothing", "%00", "caves/upper.th\0.txt", "\u65E5\u672C\u8A9E.thconfig",
        };
        var before = Snapshot(_scratch);
        foreach (var path in pool)
        {
            // A fresh host each time: a load that succeeds must not change the folder the others run in.
            var outcome = await Dispatcher().CallAsync("load_workspace", JsonSerializer.SerializeToElement(new { path }));
            if (Violation("load_workspace", wellTyped: true, outcome) is { } problem) failures.Add($"'{Short(path)}': {problem}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(Snapshot(_scratch).SequenceEqual(before), "loading a hostile path changed files");
    }
}
