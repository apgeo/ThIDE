using Therion.Build.Automation;
using Therion.Core;

namespace Therion.Build.Tests;

public class TaskCatalogTests
{
    [Fact]
    public void Loads_the_three_kinds_and_reports_incomplete_tasks()
    {
        var (tasks, errors) = TaskCatalog.Load("""
            { "version": 1, "tasks": [
              { "id": "all", "title": "Build all", "type": "shell", "command": "therion", "args": ["all.thconfig"], "problemMatcher": "therion" },
              { "id": "batch", "type": "builtin", "builtin": "batch-build", "entries": ["a.thconfig"] },
              { "id": "trip", "type": "macro", "macro": "end-of-trip" },
              { "id": "bad", "type": "shell" },
              { "id": "all", "command": "x" },
              { "id": "odd", "command": "x", "problemMatcher": "gcc" },
            ] }
            """);

        Assert.Equal(["all", "batch", "trip"], tasks.Select(t => t.Id));
        Assert.Equal("batch", tasks[1].Title);   // title defaults to the id
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void Shell_spec_quotes_values_in_a_command_line_but_not_in_arguments()
    {
        var ctx = new HookContext(HookEvent.FileSaved, "/c/My Cave/a.th", "/c", "/c/a.thconfig");
        var line = new TaskDefinition("t", "t", Command: "lint ${file}");
        var argv = new TaskDefinition("t", "t", Command: "therion", Args: ["${activeThconfig}"], Cwd: "${workspaceRoot}/out");

        var s1 = TaskCatalog.Spec(line, ctx);
        var s2 = TaskCatalog.Spec(argv, ctx);

        Assert.True(s1.UseShell);
        Assert.Equal("lint " + ShellQuote.ForArgument("/c/My Cave/a.th"), s1.FileName);
        Assert.Equal("/c", s1.WorkingDirectory);
        Assert.False(s2.UseShell);
        Assert.Equal(["/c/a.thconfig"], s2.Args);
        Assert.Equal("/c/out", s2.WorkingDirectory);
        Assert.Equal("t", s2.Environment!["THIDE_TASK"]);
    }

    [Fact]
    public void File_line_matcher_reads_position_severity_and_resolves_relative_paths()
    {
        var d = ProblemMatchers.Match("file-line", "caves/a.th:12:3: warning: odd shot", false, "/c", "lint")!;

        Assert.Equal("/c/caves/a.th".Replace('/', Path.DirectorySeparatorChar), d.Span.FilePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(12, d.Span.Start.Line);
        Assert.Equal(3, d.Span.Start.Column);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("task:lint", d.Code);
        Assert.Null(ProblemMatchers.Match("file-line", "just text", false, "/c", "lint"));
        Assert.Null(ProblemMatchers.Match(null, "a.th:1: x", false, "/c", "lint"));
    }
}
