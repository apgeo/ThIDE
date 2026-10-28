using Therion.Build.Automation;

namespace Therion.Build.Tests;

public class ProcessRunnerTests
{
    private sealed class Lines : IProgress<ProcessLine>
    {
        public readonly List<ProcessLine> Seen = [];
        public void Report(ProcessLine value) { lock (Seen) Seen.Add(value); }
    }

    private static ProcessSpec Shell(string unix, string windows, TimeSpan? timeout = null, Dictionary<string, string>? env = null) =>
        new(OperatingSystem.IsWindows() ? windows : unix, [], Timeout: timeout, Environment: env, UseShell: true);

    [Fact]
    public async Task Reports_exit_code_and_both_streams()
    {
        var lines = new Lines();

        var outcome = await ProcessRunner.RunAsync(Shell("echo out; echo err 1>&2; exit 3", "echo out & echo err 1>&2 & exit /b 3"), lines);

        Assert.Equal(3, outcome.ExitCode);
        Assert.False(outcome.Succeeded);
        Assert.Contains(new ProcessLine("out", false), lines.Seen);
        Assert.Contains(lines.Seen, l => l.IsError && l.Text.Trim() == "err");
    }

    [Fact]
    public async Task Passes_the_environment()
    {
        var lines = new Lines();

        var outcome = await ProcessRunner.RunAsync(Shell("echo \"$THIDE_EVENT\"", "echo %THIDE_EVENT%", env: new() { ["THIDE_EVENT"] = "FileSaved" }), lines);

        Assert.True(outcome.Succeeded);
        Assert.Equal("FileSaved", lines.Seen.Single().Text.Trim());
    }

    [Fact]
    public async Task Timeout_kills_the_process()
    {
        var outcome = await ProcessRunner.RunAsync(Shell("sleep 30", "ping -n 30 127.0.0.1 > nul", TimeSpan.FromMilliseconds(300)));

        Assert.True(outcome.TimedOut);
        Assert.False(outcome.Cancelled);
        Assert.True(outcome.Elapsed < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Cancellation_kills_the_process()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var outcome = await ProcessRunner.RunAsync(Shell("sleep 30", "ping -n 30 127.0.0.1 > nul"), ct: cts.Token);

        Assert.True(outcome.Cancelled);
        Assert.False(outcome.TimedOut);
    }

    [Fact]
    public async Task A_missing_program_is_a_start_error_not_an_exception()
    {
        var outcome = await ProcessRunner.RunAsync(new ProcessSpec("/no/such/program-thide", []));

        Assert.NotNull(outcome.StartError);
    }

    [Theory]
    [InlineData("plain", false, "'plain'")]
    [InlineData("it's", false, "'it'\\''s'")]
    [InlineData("a \"b\"", true, "\"a \"\"b\"\"\"")]
    public void Quotes_for_the_platform_shell(string value, bool windows, string expected) =>
        Assert.Equal(expected, ShellQuote.ForArgument(value, windows));

    [Fact]
    public async Task A_quoted_path_with_spaces_and_quotes_reaches_the_command_whole()
    {
        if (OperatingSystem.IsWindows()) return; // cmd's own parsing differs; covered by the quoting test
        var lines = new Lines();
        var value = "My Cave's \"plan\".th";

        await ProcessRunner.RunAsync(new ProcessSpec("printf '%s\\n' " + ShellQuote.ForArgument(value), [], UseShell: true), lines);

        Assert.Equal(value, lines.Seen.Single().Text);
    }
}
