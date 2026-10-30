// `therion-cli report` — the HTML survey report (totals, survey tree, team, dates, fixed points)
// the app's Survey report command and the generate_report tool build, headless.

using Therion.Semantics;
using Therion.Workspace;

namespace Therion.Cli;

public static class ReportVerb
{
    /// <summary>
    /// <c>report &lt;file|thconfig|folder&gt; [--out report.html] [--title name]</c>. Returns 0 ok,
    /// 1 the project did not load or the write failed, 2 usage error.
    /// </summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? path = null, output = null, title = null;
        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Count: output = args[++i]; break;
                case "--title" when i + 1 < args.Count: title = args[++i]; break;
                default:
                    if (path is null && !args[i].StartsWith("--", StringComparison.Ordinal)) path = args[i];
                    else { stderr.WriteLine($"error: unexpected argument '{args[i]}'."); return 2; }
                    break;
            }
        }
        if (path is null) { stderr.WriteLine("error: 'report' requires a file, thconfig or folder."); return 2; }
        if (!File.Exists(path) && !Directory.Exists(path)) { stderr.WriteLine($"error: file not found: {path}"); return 2; }

        WorkspaceSemanticModel model;
        try { model = (await WorkspaceLoader.OpenAsync(path)).BuildSemanticModel(); }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: failed to load '{path}': {ex.Message}");
            return 1;
        }

        // The same default title as the tool: the project folder's name.
        var full = Path.GetFullPath(path);
        var folder = Directory.Exists(full) ? full : Path.GetDirectoryName(full)!;
        var html = SurveyReport.BuildHtml(model, string.IsNullOrWhiteSpace(title) ? Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : title);
        if (output is null) { stdout.Write(html); return 0; }
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(output, html);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"error: {output}: {ex.Message}");
            return 1;
        }
        stderr.WriteLine($"wrote {output}");
        return 0;
    }
}
