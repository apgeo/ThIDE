// Tasks: named commands a project keeps in its config folder (tasks.json) — a shell command, a
// built-in action (build, batch build) or a macro — run from the Tools menu or the command palette.
// A problem matcher turns lines of a task's output into diagnostics.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Therion.Core;

namespace Therion.Build.Automation;

public enum TaskKind { Shell, Builtin, Macro }

/// <param name="Command">Shell: the program, or the whole command line when <paramref name="Args"/> is empty.</param>
/// <param name="Builtin">Builtin: <c>build</c> or <c>batch-build</c>.</param>
/// <param name="Entries">Builtin: the thconfigs to build (default: the active one).</param>
/// <param name="ProblemMatcher"><c>therion</c>, <c>file-line</c> or null.</param>
public sealed record TaskDefinition(
    string Id,
    string Title,
    TaskKind Type = TaskKind.Shell,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    string? Cwd = null,
    IReadOnlyDictionary<string, string>? Env = null,
    string? ProblemMatcher = null,
    string? Group = null,
    string? Builtin = null,
    IReadOnlyList<string>? Entries = null,
    string? Macro = null,
    int TimeoutSeconds = 3600);

public static partial class TaskCatalog
{
    private sealed class FileDto
    {
        public int Version { get; set; } = 1;
        public List<TaskDefinition>? Tasks { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads tasks.json. Tasks that are incomplete are reported and left out; the rest load.</summary>
    public static (IReadOnlyList<TaskDefinition> Tasks, IReadOnlyList<string> Errors) Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ([], []);
        FileDto? dto;
        try { dto = JsonSerializer.Deserialize<FileDto>(json, Json); }
        catch (JsonException ex) { return ([], [$"tasks.json (line {(ex.LineNumber ?? 0) + 1}): {ex.Message}"]); }

        var tasks = new List<TaskDefinition>();
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in dto?.Tasks ?? [])
        {
            string? problem =
                string.IsNullOrWhiteSpace(t.Id) ? "a task has no id"
                : !ids.Add(t.Id) ? $"task '{t.Id}' is defined twice"
                : t.Type == TaskKind.Shell && string.IsNullOrWhiteSpace(t.Command) ? $"task '{t.Id}' has no command"
                : t.Type == TaskKind.Builtin && t.Builtin is not ("build" or "batch-build") ? $"task '{t.Id}': builtin must be build or batch-build"
                : t.Type == TaskKind.Macro && string.IsNullOrWhiteSpace(t.Macro) ? $"task '{t.Id}' names no macro"
                : t.ProblemMatcher is not (null or "therion" or "file-line") ? $"task '{t.Id}': unknown problem matcher '{t.ProblemMatcher}'"
                : null;
            if (problem is null) tasks.Add(t with { Title = string.IsNullOrWhiteSpace(t.Title) ? t.Id : t.Title });
            else errors.Add("tasks.json: " + problem);
        }
        return (tasks, errors);
    }

    /// <summary>
    /// The process to start for a shell task: with arguments, the program and its arguments as given
    /// (variables expanded, nothing quoted); without, the command as a shell line (values quoted).
    /// </summary>
    public static ProcessSpec Spec(TaskDefinition task, HookContext ctx)
    {
        var cwd = task.Cwd is { Length: > 0 } c ? HookVariables.Expand(c, ctx, v => v) : ctx.WorkspaceRoot;
        var env = HookVariables.Environment(ctx);
        env["THIDE_TASK"] = task.Id;
        foreach (var (k, v) in task.Env ?? new Dictionary<string, string>()) env[k] = HookVariables.Expand(v, ctx, x => x);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, task.TimeoutSeconds));
        return task.Args is { Count: > 0 } args
            ? new ProcessSpec(HookVariables.Expand(task.Command!, ctx, v => v), args.Select(a => HookVariables.Expand(a, ctx, v => v)).ToList(), cwd, env, timeout)
            : new ProcessSpec(HookVariables.Expand(task.Command!, ctx, v => ShellQuote.ForArgument(v)), [], cwd, env, timeout, UseShell: true);
    }
}

public static partial class ProblemMatchers
{
    [GeneratedRegex(@"^(?<file>[^:\r\n]+?):(?<line>\d+)(?::(?<col>\d+))?:\s*(?<msg>.*)$")]
    private static partial Regex FileLine();

    private static readonly HeuristicTherionOutputParser Therion = new();

    /// <summary>
    /// A diagnostic for <paramref name="line"/> under the named matcher, or null when the line says
    /// nothing about a source position. Relative paths are taken from <paramref name="cwd"/>.
    /// </summary>
    public static Diagnostic? Match(string? matcher, string line, bool isError, string? cwd, string taskId)
    {
        switch (matcher)
        {
            case "therion":
                var classified = Therion.Classify(line, isError);
                if (classified.Span is not { } span || string.IsNullOrEmpty(span.FilePath)) return null;
                var full = Rooted(span.FilePath, cwd);
                return Diagnostic.Create("task:" + taskId, classified.Severity, classified.Text, span with { FilePath = full });
            case "file-line":
                var m = FileLine().Match(line);
                if (!m.Success) return null;
                int ln = int.Parse(m.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture);
                int col = m.Groups["col"].Success ? int.Parse(m.Groups["col"].Value, System.Globalization.CultureInfo.InvariantCulture) : 1;
                var msg = m.Groups["msg"].Value;
                var severity = msg.StartsWith("warning", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Warning
                             : msg.StartsWith("info", StringComparison.OrdinalIgnoreCase) || msg.StartsWith("note", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Info
                             : DiagnosticSeverity.Error;
                var at = new SourceLocation(ln, col);
                return Diagnostic.Create("task:" + taskId, severity, msg, new SourceSpan(Rooted(m.Groups["file"].Value.Trim(), cwd), at, at, 0, 0));
            default:
                return null;
        }
    }

    private static string Rooted(string path, string? cwd) =>
        Path.IsPathRooted(path) || cwd is null ? path : Path.GetFullPath(Path.Combine(cwd, path));
}
