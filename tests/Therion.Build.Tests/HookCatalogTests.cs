using Therion.Build.Automation;

namespace Therion.Build.Tests;

public class HookCatalogTests
{
    private static string Sh(string v) => ShellQuote.ForArgument(v, windows: false);

    [Fact]
    public void Legacy_settings_come_first_detached_then_user_then_project()
    {
        var all = HookCatalog.Merge("open {file}", null, "build", [new HookDefinition(HookEvent.FileSaved, "u")], [new HookDefinition(HookEvent.FileSaved, "p")]);

        Assert.Equal([HookSource.Legacy, HookSource.Legacy, HookSource.User, HookSource.Project], all.Select(h => h.Source));
        Assert.All(all.Where(h => h.Source == HookSource.Legacy), h => Assert.True(h.Detached));
        Assert.Equal([HookEvent.FileOpened, HookEvent.BuildStarted], all.Take(2).Select(h => h.Event));
        Assert.Equal(["u", "p"], HookCatalog.For(all, HookEvent.FileSaved).Select(h => h.Command));
    }

    [Fact]
    public void Project_file_reads_with_comments_and_event_names()
    {
        var (hooks, error) = HookCatalog.LoadProject("""
            { "version": 1, "hooks": [
                // publish after a good build
                { "event": "BuildFinished", "command": "rsync -a output/ server:/caves/", "timeoutSeconds": 120 },
                { "event": "fileSaved", "task": "lint", "enabled": false },
            ] }
            """);

        Assert.Null(error);
        Assert.Equal(2, hooks.Count);
        Assert.Equal(120, hooks[0].TimeoutSeconds);
        Assert.Equal(HookEvent.FileSaved, hooks[1].Event);
        Assert.Empty(HookCatalog.For(hooks, HookEvent.FileSaved));
    }

    [Fact]
    public void A_hook_needs_exactly_one_of_command_and_task()
    {
        var (hooks, error) = HookCatalog.LoadProject("""{ "hooks": [ { "event": "FileSaved" }, { "event": "FileSaved", "command": "ok" } ] }""");
        Assert.NotNull(error);
        Assert.Equal("ok", Assert.Single(hooks).Command);
        Assert.NotNull(HookCatalog.LoadProject("{ broken").Error);
    }

    [Fact]
    public void A_hook_listed_twice_runs_twice()
    {
        var (hooks, error) = HookCatalog.LoadProject("""{ "hooks": [ { "event": "FileSaved", "command": "ok" }, { "event": "FileSaved", "command": "ok" } ] }""");
        Assert.Null(error);
        Assert.Equal(2, hooks.Count);
    }

    [Fact]
    public void Variables_are_quoted_once()
    {
        var ctx = new HookContext(HookEvent.FileSaved, "/caves/My Cave/it's.th", "/caves", "/caves/a.thconfig");

        Assert.Equal($"lint {Sh("/caves/My Cave/it's.th")}", HookVariables.Expand("lint {file}", ctx, Sh));
        Assert.Equal($"lint {Sh("/caves/My Cave/it's.th")}", HookVariables.Expand("lint \"{file}\"", ctx, Sh));
        Assert.Equal($"cd {Sh("/caves/My Cave")} && x {Sh("it's.th")} {Sh("/caves")} ${{unknown}}",
            HookVariables.Expand("cd ${fileDirname} && x ${fileBasename} ${workspaceRoot} ${unknown}", ctx, Sh));
        Assert.Equal(Sh("v"), HookVariables.Expand("${env:X}", ctx, Sh, n => n == "X" ? "v" : null));
    }

    [Fact]
    public void Environment_carries_the_build_outcome()
    {
        var outcome = new BuildOutcome("/c/a.thconfig", false, false, 2, 3, 1, ["/c/a.pdf", "/c/a.lox"], "/c/a.log", TimeSpan.FromSeconds(1.5));

        var env = HookVariables.Environment(new HookContext(HookEvent.BuildFailed, Entry: "/c/a.thconfig", Outcome: outcome));

        Assert.Equal("BuildFailed", env["THIDE_EVENT"]);
        Assert.Equal("2", env["THIDE_BUILD_EXIT"]);
        Assert.Equal("3", env["THIDE_BUILD_ERRORS"]);
        Assert.Equal("1500", env["THIDE_ELAPSED_MS"]);
        Assert.Equal($"/c/a.pdf{Path.PathSeparator}/c/a.lox", env["THIDE_OUTPUTS"]);
    }

    [Fact]
    public void A_value_that_looks_like_a_variable_is_not_expanded_again()
    {
        var ctx = new HookContext(HookEvent.FileSaved, "/c/${workspaceRoot}/a.th", "/ws path");

        Assert.Equal("x " + Sh("/c/${workspaceRoot}/a.th"), HookVariables.Expand("x {file}", ctx, Sh));
        Assert.Equal("x " + Sh("/c/${workspaceRoot}/a.th"), HookVariables.Expand("x ${file}", ctx, Sh));
    }
}
