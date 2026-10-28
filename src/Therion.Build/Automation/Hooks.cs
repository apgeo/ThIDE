// Hooks: commands run when something happens in the IDE. They come from three places, in this
// order: the three original single-command settings (open, save, build — still detached, so nothing
// changes for people who use them), the user's hook list in the settings, and the project's
// `hooks.json` in its config folder (run only in a trusted folder).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Therion.Build.Automation;

public enum HookEvent { WorkspaceOpened, FileOpened, FileSaved, BuildStarted, BuildFinished, BuildFailed }

public enum HookSource { Legacy, User, Project }

/// <param name="Command">A shell command line (variables expanded, paths quoted).</param>
/// <param name="Task">Instead of a command: the id of a task to run.</param>
/// <param name="Detached">Start and forget: no output, no timeout, nothing waits.</param>
public sealed record HookDefinition(
    HookEvent Event,
    string? Command = null,
    string? Task = null,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Env = null,
    int TimeoutSeconds = 60,
    bool Detached = false,
    bool Enabled = true)
{
    [JsonIgnore] public HookSource Source { get; init; } = HookSource.User;
}

/// <summary>What happened, for variables and the hook's environment.</summary>
public sealed record HookContext(
    HookEvent Event,
    string? File = null,
    string? WorkspaceRoot = null,
    string? ActiveThconfig = null,
    string? Entry = null,
    BuildOutcome? Outcome = null);

public static class HookCatalog
{
    private sealed class FileDto
    {
        public int Version { get; set; } = 1;
        public List<HookDefinition>? Hooks { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads a project's hooks.json; a broken file is an error value.</summary>
    public static (IReadOnlyList<HookDefinition> Hooks, string? Error) LoadProject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ([], null);
        try
        {
            var dto = JsonSerializer.Deserialize<FileDto>(json, Json);
            var hooks = (dto?.Hooks ?? []).Select(h => h with { Source = HookSource.Project }).ToList();
            var bad = hooks.FirstOrDefault(h => string.IsNullOrWhiteSpace(h.Command) == string.IsNullOrWhiteSpace(h.Task));
            return bad is null ? (hooks, null) : ([], $"hooks.json: each hook needs either a command or a task ({bad.Event})");
        }
        catch (JsonException ex) { return ([], $"hooks.json (line {(ex.LineNumber ?? 0) + 1}): {ex.Message}"); }
    }

    /// <summary>All hooks in run order: the original settings, then the user's list, then the project's.</summary>
    public static IReadOnlyList<HookDefinition> Merge(
        string? legacyOpen, string? legacySave, string? legacyBuild,
        IEnumerable<HookDefinition> user, IEnumerable<HookDefinition> project)
    {
        var list = new List<HookDefinition>();
        void Legacy(HookEvent ev, string? command)
        {
            if (!string.IsNullOrWhiteSpace(command))
                list.Add(new HookDefinition(ev, command, Detached: true) { Source = HookSource.Legacy });
        }
        Legacy(HookEvent.FileOpened, legacyOpen);
        Legacy(HookEvent.FileSaved, legacySave);
        Legacy(HookEvent.BuildStarted, legacyBuild);
        list.AddRange(user.Select(h => h with { Source = HookSource.User }));
        list.AddRange(project.Select(h => h with { Source = HookSource.Project }));
        return list;
    }

    /// <summary>The enabled hooks for an event, in run order.</summary>
    public static IEnumerable<HookDefinition> For(IEnumerable<HookDefinition> hooks, HookEvent ev) =>
        hooks.Where(h => h.Enabled && h.Event == ev);
}

public static class HookVariables
{
    /// <summary>
    /// Expands <c>{file}</c> and <c>${name}</c> variables. With <paramref name="quote"/> (for a shell
    /// command line) every value is quoted as one shell word, so a path with spaces or quotes
    /// stays one argument; a <c>{file}</c> the user already wrapped in quotes is not quoted twice.
    /// Unknown variables are left as written.
    /// </summary>
    public static string Expand(string template, HookContext ctx, Func<string, string> quote, Func<string, string?>? env = null)
    {
        string Q(string? v) => quote(v ?? "");
        var file = ctx.File;
        var s = template
            .Replace("\"{file}\"", Q(file)).Replace("'{file}'", Q(file)).Replace("{file}", Q(file));
        return System.Text.RegularExpressions.Regex.Replace(s, @"\$\{(env:)?([A-Za-z_][A-Za-z0-9_]*)\}", m =>
        {
            if (m.Groups[1].Success) return Q((env ?? System.Environment.GetEnvironmentVariable)(m.Groups[2].Value));
            return m.Groups[2].Value switch
            {
                "file" => Q(file),
                "fileDirname" => Q(file is null ? null : Path.GetDirectoryName(file)),
                "fileBasename" => Q(file is null ? null : Path.GetFileName(file)),
                "workspaceRoot" => Q(ctx.WorkspaceRoot),
                "activeThconfig" => Q(ctx.ActiveThconfig),
                "entry" => Q(ctx.Entry),
                _ => m.Value,
            };
        });
    }

    /// <summary>The THIDE_* environment every hook gets.</summary>
    public static Dictionary<string, string> Environment(HookContext ctx)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["THIDE_EVENT"] = ctx.Event.ToString(),
            ["THIDE_FILE"] = ctx.File ?? "",
            ["THIDE_WORKSPACE"] = ctx.WorkspaceRoot ?? "",
            ["THIDE_THCONFIG"] = ctx.ActiveThconfig ?? "",
            ["THIDE_ENTRY"] = ctx.Entry ?? "",
        };
        if (ctx.Outcome is { } o)
        {
            env["THIDE_BUILD_EXIT"] = o.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            env["THIDE_BUILD_ERRORS"] = o.Errors.ToString(System.Globalization.CultureInfo.InvariantCulture);
            env["THIDE_BUILD_WARNINGS"] = o.Warnings.ToString(System.Globalization.CultureInfo.InvariantCulture);
            env["THIDE_BUILD_LOG"] = o.LogPath ?? "";
            env["THIDE_OUTPUTS"] = string.Join(Path.PathSeparator, o.Artifacts);
            env["THIDE_ELAPSED_MS"] = ((long)o.Elapsed.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return env;
    }
}
