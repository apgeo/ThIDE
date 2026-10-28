// `therion-cli new` — create a project from a project template, headless: a built-in id, an
// imported template's id, or a template folder.

using System.Globalization;
using Therion.Workspace;
using Therion.Workspace.Templates;

namespace Therion.Cli;

public static class NewVerb
{
    /// <summary>The library ThIDE imports templates into (shared with the app).</summary>
    public static string DefaultLibrary => Path.Combine(ProjectSidecar.DirectoryFor("templates"), "projects");

    /// <summary>
    /// <c>new --list</c> or <c>new &lt;id|folder&gt; --out &lt;dir&gt; [--var k=v]…</c>. Returns 0 ok,
    /// 1 write failure, 2 usage or value error.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, string? library = null, DateTime? now = null)
    {
        library ??= DefaultLibrary;
        var templates = Available(library);
        if (args.Contains("--list"))
        {
            foreach (var t in templates)
                stdout.WriteLine($"{t.Id,-20} {t.Title} — {string.Join(", ", t.Variables.Select(v => $"{v.Id}={v.Default}"))}");
            return 0;
        }
        if (args.Count < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            stderr.WriteLine("error: 'new' needs a template id or folder (see 'new --list').");
            return 2;
        }

        ProjectTemplate? template = templates.FirstOrDefault(t => t.Id == args[1]);
        if (template is null && Directory.Exists(args[1]))
        {
            var (loaded, error) = ProjectTemplateEngine.LoadFolder(args[1]);
            if (loaded is null) { stderr.WriteLine($"error: {error}"); return 2; }
            template = loaded;
        }
        if (template is null) { stderr.WriteLine($"error: no template '{args[1]}' (see 'new --list')."); return 2; }

        string? output = null;
        var stamp = now ?? DateTime.Now;
        var values = ProjectTemplateEngine.Defaults(template, stamp);
        for (int i = 2; i < args.Count; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Count) output = args[++i];
            else if (args[i] == "--var" && i + 1 < args.Count)
            {
                var kv = args[++i];
                int eq = kv.IndexOf('=');
                if (eq <= 0) { stderr.WriteLine($"error: --var expects key=value, got '{kv}'."); return 2; }
                values[kv[..eq]] = kv[(eq + 1)..];
            }
            else { stderr.WriteLine($"error: unexpected argument '{args[i]}'."); return 2; }
        }
        if (output is null) { stderr.WriteLine("error: 'new' needs --out <dir>."); return 2; }

        var (plan, errors) = ProjectTemplateEngine.Plan(template, values, stamp);
        if (plan is null)
        {
            foreach (var e in errors) stderr.WriteLine($"error: {e}");
            return 2;
        }
        try
        {
            Directory.CreateDirectory(output);
            if (ProjectTemplateEngine.Write(plan, template.Folder, output) is { } problem) { stderr.WriteLine($"error: {problem}"); return 1; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { stderr.WriteLine($"error: {ex.Message}"); return 1; }

        foreach (var f in plan.Files) stdout.WriteLine(Path.Combine(output, f.RelativePath));
        if (plan.ThconfigRelativePath.Length > 0)
            stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"thconfig: {Path.Combine(output, plan.ThconfigRelativePath)}"));
        return 0;
    }

    private static List<ProjectTemplate> Available(string library)
    {
        var list = ProjectTemplateEngine.BuiltIns.ToList();
        if (Directory.Exists(library))
            foreach (var dir in Directory.EnumerateDirectories(library).Order(StringComparer.Ordinal))
                if (ProjectTemplateEngine.LoadFolder(dir).Template is { } t) list.Add(t);
        return list;
    }
}
