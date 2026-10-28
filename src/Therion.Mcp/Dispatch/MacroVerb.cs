// `therion-mcp macro list|run` — macros without an MCP host or a model: the same tools, called
// in-process. `run` previews unless --apply is given, so it is safe in CI.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Therion.Mcp.Dispatch;
using Therion.Workspace;

namespace Therion.Mcp.Dispatch;

public static class MacroVerb
{
    /// <summary>
    /// <c>macro list [--workspace p]</c> or <c>macro run &lt;name|file&gt; --workspace p [--var k=v]… [--apply] [--json]</c>.
    /// Returns 0 ok, 1 a step failed, 2 usage error.
    /// </summary>
    /// <param name="buildProvider">Builds the service provider (the host's DI container implementation).</param>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr,
        Func<IServiceCollection, IServiceProvider> buildProvider, string? userMacroFolder = null, CancellationToken ct = default)
    {
        string? workspace = null, target = null;
        bool apply = false, json = false;
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        string verb = args.Count > 1 ? args[1] : "";
        for (int i = 2; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--workspace" when i + 1 < args.Count: workspace = args[++i]; break;
                case "--apply": apply = true; break;
                case "--json": json = true; break;
                case "--var" when i + 1 < args.Count:
                    var kv = args[++i];
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) { stderr.WriteLine($"error: --var expects key=value, got '{kv}'."); return 2; }
                    vars[kv[..eq]] = kv[(eq + 1)..];
                    break;
                default:
                    if (target is null && !args[i].StartsWith("--", StringComparison.Ordinal)) target = args[i];
                    else { stderr.WriteLine($"error: unexpected argument '{args[i]}'."); return 2; }
                    break;
            }
        }

        var root = workspace is null ? null : Directory.Exists(workspace) ? Path.GetFullPath(workspace) : Path.GetDirectoryName(Path.GetFullPath(workspace));
        userMacroFolder ??= Path.Combine(ProjectSidecar.DirectoryFor("macros"));
        var (user, userErrors) = MacroCatalog.LoadFolder(userMacroFolder);
        // The project's macros wherever the user keeps project settings: in the project or in app data.
        var (project, projectErrors) = MacroCatalog.LoadFolder(ProjectLocalConfig.PathFor(root, ProjectConfigLocation.ProjectFolder, "macros"));
        var (privateProject, privateErrors) = MacroCatalog.LoadFolder(ProjectLocalConfig.PathFor(root, ProjectConfigLocation.AppData, "macros"));
        foreach (var e in userErrors.Concat(projectErrors).Concat(privateErrors)) stderr.WriteLine("warning: " + e);
        var all = MacroCatalog.Merge(user, privateProject.Concat(project)).ToDictionary(m => m.Macro.Name, m => m.Macro, StringComparer.Ordinal);

        if (verb == "list")
        {
            foreach (var m in all.Values.OrderBy(m => m.Name, StringComparer.Ordinal))
                stdout.WriteLine($"{m.Name,-20} {m.Title}" + (m.Vars.Count > 0 ? "  (" + string.Join(", ", m.Vars.Select(v => v.Id)) + ")" : ""));
            return 0;
        }
        if (verb != "run" || target is null) { stderr.WriteLine("error: use 'macro list' or 'macro run <name|file> --workspace <path>'."); return 2; }
        if (workspace is null) { stderr.WriteLine("error: 'macro run' needs --workspace <path>."); return 2; }

        MacroDefinition? macro = all.GetValueOrDefault(target);
        if (macro is null && File.Exists(target))
        {
            var (m, e) = MacroCatalog.Load(Path.GetFileNameWithoutExtension(target), File.ReadAllText(target));
            if (m is null) { stderr.WriteLine("error: " + e); return 2; }
            macro = m;
        }
        if (macro is null) { stderr.WriteLine($"error: no macro '{target}' (see 'macro list')."); return 2; }

        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(workspace));
        services.AddToolServices();
        var runner = new MacroRunner(new ToolDispatcher(buildProvider(services)));
        if (root is not null) vars.TryAdd("workspaceRoot", root);
        var problems = runner.Check(macro, vars);
        if (problems.Count > 0)
        {
            foreach (var p in problems) stderr.WriteLine("error: " + p);
            return 2;
        }

        var progress = new Therion.Build.Automation.SyncProgress<StepResult>(s => { if (!json) stdout.WriteLine($"{s.StepId,-12} {s.Tool,-20} {s.Status,-9} {s.Summary}"); });
        if (!json && apply) stdout.WriteLine("Preview:");
        var preview = await runner.RunAsync(macro, vars, apply: false, progress: progress, ct: ct);
        // A failed preview is never applied (the same rule as the app).
        if (apply && preview.Failed)
        {
            stderr.WriteLine("error: the preview failed, so nothing was applied.");
            apply = false;
        }
        if (!json && apply) stdout.WriteLine("Apply:");
        var run = apply ? await runner.RunAsync(macro, vars, apply: true, preview, progress, ct) : preview;
        if (json)
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                macro = macro.Name, applied = run.Applied, failed = run.Failed,
                steps = run.Steps.Select(s => new { id = s.StepId, tool = s.Tool, status = s.Status.ToString(), summary = s.Summary, error = s.Error }),
            }, new JsonSerializerOptions { WriteIndented = true }));
        else if (!apply)
            stdout.WriteLine("Preview only: nothing was changed. Run again with --apply to make the changes.");
        return run.Failed ? 1 : 0;
    }
}
