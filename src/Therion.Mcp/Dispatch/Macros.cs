// Macros: a named list of tool calls with variables, run without a model. Every run previews first —
// tools that can (dryRun) report what they would change, read-only tools run, anything else is
// listed as skipped — and only an explicit apply makes the changes, each file guarded by the digest
// the preview saw.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Therion.Mcp.Dispatch;

public sealed record MacroVariable(string Id, string? Prompt = null, string Type = "text", string? Default = null, IReadOnlyList<string>? Choices = null);

public sealed record MacroStep(string Id, string Tool, JsonElement? Args = null, bool ContinueOnError = false);

/// <param name="Name">The macro's id: its file name without extension.</param>
public sealed record MacroDefinition(string Name, string Title, string? Description, IReadOnlyList<MacroVariable> Vars, IReadOnlyList<MacroStep> Steps);

public enum StepStatus { Previewed, Ran, Skipped, Failed }

/// <param name="Summary">A one-line description of what the step did or would do.</param>
public sealed record StepResult(string StepId, string Tool, StepStatus Status, JsonElement? Result, string? Error, string Summary);

public sealed record MacroRun(bool Applied, IReadOnlyList<StepResult> Steps)
{
    public bool Failed => Steps.Any(s => s.Status == StepStatus.Failed);
}

public static partial class MacroCatalog
{
    private sealed class Dto
    {
        public int Version { get; set; } = 1;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<MacroVariable>? Vars { get; set; }
        public List<MacroStep>? Steps { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads a macro file; a broken one is an error value.</summary>
    public static (MacroDefinition? Macro, string? Error) Load(string name, string json)
    {
        Dto? dto;
        try { dto = JsonSerializer.Deserialize<Dto>(json, Json); }
        catch (JsonException ex) { return (null, $"{name}: {ex.Message}"); }
        if (dto?.Steps is not { Count: > 0 } steps) return (null, $"{name}: a macro needs at least one step");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in steps)
        {
            if (string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Tool)) return (null, $"{name}: each step needs an id and a tool");
            if (!ids.Add(s.Id)) return (null, $"{name}: step '{s.Id}' is defined twice");
        }
        return (new MacroDefinition(name, dto.Title ?? name, dto.Description, dto.Vars ?? [], steps), null);
    }

    /// <summary>Every macro in a folder (<c>*.json</c>), plus what could not be read.</summary>
    public static (IReadOnlyList<MacroDefinition> Macros, IReadOnlyList<string> Errors) LoadFolder(string? folder)
    {
        var macros = new List<MacroDefinition>();
        var errors = new List<string>();
        if (folder is null || !Directory.Exists(folder)) return (macros, errors);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            string json;
            try { json = File.ReadAllText(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(ex.Message); continue; }
            var (m, e) = Load(Path.GetFileNameWithoutExtension(file), json);
            if (m is not null) macros.Add(m); else errors.Add(e!);
        }
        return (macros, errors);
    }

    /// <summary>Built-ins, then user macros, then project macros: a later one with the same name wins.</summary>
    public static IReadOnlyList<(MacroDefinition Macro, bool FromProject)> Merge(IEnumerable<MacroDefinition> user, IEnumerable<MacroDefinition> project)
    {
        var all = new Dictionary<string, (MacroDefinition, bool)>(StringComparer.Ordinal);
        foreach (var m in BuiltIns) all[m.Name] = (m, false);
        foreach (var m in user) all[m.Name] = (m, false);
        foreach (var m in project) all[m.Name] = (m, true);
        return all.Values.OrderBy(m => m.Item1.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The macros ThIDE ships.</summary>
    public static IReadOnlyList<MacroDefinition> BuiltIns { get; } =
    [
        Load("release-check", """
            { "title": "Release check", "description": "Diagnostics, a build, then the HTML report.",
              "steps": [
                { "id": "diag", "tool": "get_diagnostics", "args": { "minSeverity": "warning" } },
                { "id": "build", "tool": "run_build", "continueOnError": true },
                { "id": "report", "tool": "generate_report", "args": { "target": "output/report.html" } } ] }
            """).Macro!,
        Load("end-of-trip", """
            { "title": "End of trip", "description": "Convert a Survex or Compass export that is inside the project (incoming/, say) to Therion, then list the project's warnings. Link the new file with Tools > Link survey file.",
              "vars": [ { "id": "source", "prompt": "Survey export inside the project (.svx or .dat)", "type": "file" } ],
              "steps": [
                { "id": "imp", "tool": "import_survey", "args": { "source": "${source}", "target": "${file:basename(source)}.th" } },
                { "id": "diag", "tool": "get_diagnostics", "args": { "minSeverity": "warning" } } ] }
            """).Macro!,
    ];
}

public sealed partial class MacroRunner(ToolDispatcher dispatcher)
{
    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex Reference();

    /// <summary>
    /// The references a macro makes that cannot resolve: unknown variables, steps that come later or
    /// do not exist, unknown tools. A macro with any is not run.
    /// </summary>
    public IReadOnlyList<string> Check(MacroDefinition macro, IReadOnlyDictionary<string, string> given)
    {
        // A variable with a default counts as given.
        var vars = new Dictionary<string, string>(given, StringComparer.Ordinal);
        foreach (var v in macro.Vars) if (v.Default is not null) vars.TryAdd(v.Id, v.Default);
        var problems = new List<string>();
        var earlier = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in macro.Steps)
        {
            if (dispatcher.Find(step.Tool) is null) problems.Add($"step '{step.Id}': no tool '{step.Tool}'");
            foreach (var r in Strings(step.Args).SelectMany(s => Reference().Matches(s)).Select(m => m.Groups[1].Value))
            {
                if (r.StartsWith("steps.", StringComparison.Ordinal))
                {
                    var id = r.Split('.')[1];
                    if (!earlier.Contains(id)) problems.Add($"step '{step.Id}': '{id}' is not an earlier step");
                }
                else if (FileFunction(r) is { } arg) { if (!vars.ContainsKey(arg)) problems.Add($"step '{step.Id}': no variable '{arg}'"); }
                else if (r != "date" && !vars.ContainsKey(r)) problems.Add($"step '{step.Id}': no variable '{r}'");
            }
            earlier.Add(step.Id);
        }
        foreach (var v in macro.Vars)
            if (!vars.ContainsKey(v.Id) && v.Default is null) problems.Add($"variable '{v.Id}' has no value");
        return problems;
    }

    /// <summary>
    /// Runs the macro: a preview when <paramref name="apply"/> is false, else the real run (using
    /// the digests <paramref name="preview"/> saw, so a file changed in between is refused).
    /// </summary>
    public async Task<MacroRun> RunAsync(MacroDefinition macro, IReadOnlyDictionary<string, string> vars, bool apply,
        MacroRun? preview = null, IProgress<StepResult>? progress = null, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>(vars, StringComparer.Ordinal);
        foreach (var v in macro.Vars) if (!values.ContainsKey(v.Id) && v.Default is not null) values[v.Id] = v.Default;
        values.TryAdd("date", DateTime.Now.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture));

        var results = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var steps = new List<StepResult>();
        void Report(StepResult r) { steps.Add(r); progress?.Report(r); }

        foreach (var step in macro.Steps)
        {
            var tool = dispatcher.Find(step.Tool)!;
            // A step that reads a skipped step's result cannot run either.
            var needs = Strings(step.Args).SelectMany(s => Reference().Matches(s)).Select(m => m.Groups[1].Value)
                .Where(r => r.StartsWith("steps.", StringComparison.Ordinal)).Select(r => r.Split('.')[1]).ToList();
            if (needs.FirstOrDefault(skipped.Contains) is { } missing)
            {
                skipped.Add(step.Id);
                Report(new StepResult(step.Id, step.Tool, StepStatus.Skipped, null, null, $"needs '{missing}', which was skipped"));
                continue;
            }
            if (!apply && !tool.ReadOnly && !tool.CanPreview)
            {
                skipped.Add(step.Id);
                Report(new StepResult(step.Id, step.Tool, StepStatus.Skipped, null, null, "runs only when applied (it cannot preview)"));
                continue;
            }

            JsonObject args;
            try { args = Resolve(step.Args, values, results); }
            catch (KeyNotFoundException ex)
            {
                Report(new StepResult(step.Id, step.Tool, StepStatus.Failed, null, ex.Message, ex.Message));
                if (step.ContinueOnError) { skipped.Add(step.Id); continue; }
                break;
            }
            if (tool.HasDryRun) args["dryRun"] = !apply;
            // A `write` tool previews with write=false; applying keeps what the step asked for.
            if (tool.HasWrite && !apply) args["write"] = false;
            if (apply && tool.HasExpectedSha && !args.ContainsKey("expectedSha256")
                && preview?.Steps.FirstOrDefault(s => s.StepId == step.Id)?.Result is { } before && Digest(before) is { } sha)
                args["expectedSha256"] = sha;

            var outcome = await dispatcher.CallAsync(step.Tool, JsonSerializer.SerializeToElement(args), ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                results[step.Id] = outcome.Result;
                Report(new StepResult(step.Id, step.Tool, apply || tool.ReadOnly ? StepStatus.Ran : StepStatus.Previewed, outcome.Result, null, Summarize(outcome.Result)));
                continue;
            }
            Report(new StepResult(step.Id, step.Tool, StepStatus.Failed, outcome.Found ? outcome.Result : null, outcome.Error, outcome.Error ?? "failed"));
            if (!step.ContinueOnError) break;
            skipped.Add(step.Id);
        }
        return new MacroRun(apply, steps);
    }

    // The arguments with every ${…} replaced. A string that is exactly one step reference keeps the
    // referenced value's JSON type (a number stays a number).
    private static JsonObject Resolve(JsonElement? args, Dictionary<string, string> vars, Dictionary<string, JsonElement> results)
    {
        var node = args is { ValueKind: JsonValueKind.Object } a ? JsonNode.Parse(a.GetRawText())!.AsObject() : new JsonObject();
        return (JsonObject)Walk(node, vars, results)!;
    }

    private static JsonNode? Walk(JsonNode? node, Dictionary<string, string> vars, Dictionary<string, JsonElement> results)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList()) o[key] = Walk(o[key]?.DeepClone(), vars, results);
                return o;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++) arr[i] = Walk(arr[i]?.DeepClone(), vars, results);
                return arr;
            case JsonValue v when v.TryGetValue<string>(out var s):
                var whole = Reference().Match(s);
                if (whole.Success && whole.Length == s.Length && whole.Groups[1].Value.StartsWith("steps.", StringComparison.Ordinal))
                    return JsonNode.Parse(StepValue(whole.Groups[1].Value, results).GetRawText());
                return JsonValue.Create(Reference().Replace(s, m => Value(m.Groups[1].Value, vars, results)));
            default:
                return node;
        }
    }

    private static string Value(string reference, Dictionary<string, string> vars, Dictionary<string, JsonElement> results)
    {
        if (reference.StartsWith("steps.", StringComparison.Ordinal))
        {
            var v = StepValue(reference, results);
            return v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText();
        }
        if (reference.StartsWith("file:basename(", StringComparison.Ordinal) && FileFunction(reference) is { } a)
            return Path.GetFileNameWithoutExtension(vars[a]);
        if (reference.StartsWith("file:dir(", StringComparison.Ordinal) && FileFunction(reference) is { } d)
            return Path.GetDirectoryName(vars[d]) ?? "";
        return vars.TryGetValue(reference, out var value) ? value : throw new KeyNotFoundException($"no value for '{reference}'");
    }

    private static string? FileFunction(string reference)
    {
        var m = Regex.Match(reference, @"^file:(basename|dir)\(([A-Za-z_][A-Za-z0-9_]*)\)$");
        return m.Success ? m.Groups[2].Value : null;
    }

    private static JsonElement StepValue(string reference, Dictionary<string, JsonElement> results)
    {
        var parts = reference.Split('.');
        if (!results.TryGetValue(parts[1], out var current)) throw new KeyNotFoundException($"step '{parts[1]}' has no result");
        foreach (var p in parts.Skip(2))
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(p, out var next)) current = next;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(p, out var i) && i >= 0 && i < current.GetArrayLength()) current = current[i];
            else throw new KeyNotFoundException($"'{reference}' is not in the result of step '{parts[1]}'");
        }
        return current;
    }

    private static IEnumerable<string> Strings(JsonElement? e)
    {
        if (e is not { } el) yield break;
        switch (el.ValueKind)
        {
            case JsonValueKind.String: yield return el.GetString()!; break;
            case JsonValueKind.Object: foreach (var p in el.EnumerateObject()) foreach (var s in Strings(p.Value)) yield return s; break;
            case JsonValueKind.Array: foreach (var x in el.EnumerateArray()) foreach (var s in Strings(x)) yield return s; break;
        }
    }

    // The digest a preview saw when it touched exactly one file.
    private static string? Digest(JsonElement result)
    {
        var shas = new List<string>();
        void Find(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "sha256" && p.Value.ValueKind == JsonValueKind.String) shas.Add(p.Value.GetString()!);
                    else Find(p.Value);
                }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Find(x);
        }
        Find(result);
        return shas.Count == 1 ? shas[0] : null;
    }

    private static string Summarize(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("data", out var data)) return "ok";
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            return $"{files.GetArrayLength()} file(s)" + (data.TryGetProperty("dryRun", out var d) && d.ValueKind == JsonValueKind.True ? " would change" : " changed");
        var text = data.GetRawText();
        return text.Length > 160 ? text[..160] + "…" : text;
    }
}
