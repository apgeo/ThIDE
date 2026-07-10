using System.Collections.Immutable;
using ModelContextProtocol;
using Therion.Core;
using Therion.Mcp.Tools;
using Therion.Processing.Abstractions;

namespace Therion.Mcp.Tests;

public class BuildToolsTests
{
    [Fact]
    public async Task Build_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();

        var result = await new BuildTools(host, new StubCompiler()).RunBuild();

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    [Fact]
    public async Task A_clean_build_reports_success_and_its_artifacts()
    {
        using var fixture = FixtureWorkspace.Create();
        var artifact = fixture.PathTo("out", "cave.lox");
        var compiler = new StubCompiler
        {
            ExitCode = 0,
            Artifacts = [new OutputArtifact(artifact, "model", 4096, DateTimeOffset.UnixEpoch)],
            Lines = [Info("therion 6.4.0"), Info("done.")],
        };

        var result = await (await LoadedAsync(fixture, compiler)).RunBuild();

        Assert.True(result.Ok, result.Error?.Message);
        Assert.True(result.Data!.Success);
        Assert.Equal(0, result.Data.ExitCode);
        Assert.Equal("project.thconfig", result.Data.EntryPoint);

        var reported = Assert.Single(result.Data.Artifacts);
        Assert.Equal("out/cave.lox", reported.Path);
        Assert.Equal("model", reported.Kind);
        Assert.Equal(4096, reported.SizeBytes);
    }

    /// <summary>Therion reporting errors is a *successful* tool call: the caller wanted to know.</summary>
    [Fact]
    public async Task A_failed_compile_is_a_result_not_an_error()
    {
        using var fixture = FixtureWorkspace.Create();
        var compiler = new StubCompiler
        {
            ExitCode = 1,
            Diagnostics = [Diagnostic.Create("TH_EXT_001", DiagnosticSeverity.Error, "station does not exist", SourceSpan.None)],
            Lines = [Error("station 5 does not exist -- E65a")],
        };

        var result = await (await LoadedAsync(fixture, compiler)).RunBuild();

        Assert.True(result.Ok);
        Assert.False(result.Data!.Success);
        Assert.Equal(1, result.Data.ExitCode);
        Assert.Equal("station does not exist", Assert.Single(result.Data.Diagnostics).Message);
    }

    /// <summary>A missing compiler is actionable, not "exit code -1".</summary>
    [Fact]
    public async Task A_missing_therion_says_where_it_looked()
    {
        using var fixture = FixtureWorkspace.Create();
        var compiler = new StubCompiler
        {
            ExitCode = -1,
            Diagnostics = [Diagnostic.Create("TH_BUILD_001", DiagnosticSeverity.Error, "not found", SourceSpan.None)],
        };

        var result = await (await LoadedAsync(fixture, compiler)).RunBuild();

        Assert.Equal(ToolErrorCodes.ToolNotFound, result.Error!.Code);
        Assert.Contains("PATH", result.Error.Message);
        Assert.Contains("override", result.Error.Message);
    }

    [Fact]
    public async Task Every_output_line_is_reported_as_progress()
    {
        using var fixture = FixtureWorkspace.Create();
        var compiler = new StubCompiler { Lines = [Info("one"), Warning("two"), Error("three")] };
        var seen = new List<ProgressNotificationValue>();

        await (await LoadedAsync(fixture, compiler)).RunBuild(progress: new SyncProgress(seen.Add));

        Assert.Equal(3, seen.Count);
        Assert.Equal(["one", "two", "three"], seen.Select(p => p.Message));
        Assert.Equal([1, 2, 3], seen.Select(p => (int)p.Progress));
        Assert.All(seen, p => Assert.Null(p.Total));   // Therion never says how much is left
    }

    /// <summary>Warnings and errors come first: a broken build hides its reason in the middle of the log.</summary>
    [Fact]
    public async Task Output_leads_with_the_warnings_and_errors_then_the_tail()
    {
        using var fixture = FixtureWorkspace.Create();
        var lines = Enumerable.Range(0, 100).Select(i => Info($"chatter {i}")).ToList();
        lines.Insert(10, Error("the real problem"));
        var compiler = new StubCompiler { Lines = lines };

        var result = await (await LoadedAsync(fixture, compiler)).RunBuild();

        Assert.Equal("the real problem", result.Data!.Output[0]);
        Assert.Equal("chatter 99", result.Data.Output[^1]);   // the tail, not the head
        Assert.Equal(101, result.Data.OutputLines);
        Assert.True(result.Data.Truncated);
    }

    /// <summary>Therion prints blank lines around its errors, and the classifier gives them its severity.</summary>
    [Fact]
    public async Task Blank_lines_do_not_lead_the_summary()
    {
        using var fixture = FixtureWorkspace.Create();
        var compiler = new StubCompiler { Lines = [Error(""), Error("the real problem"), Info("  ")] };

        var result = await (await LoadedAsync(fixture, compiler)).RunBuild();

        Assert.Equal(["the real problem"], result.Data!.Output);
        Assert.Equal(3, result.Data.OutputLines);   // the count is what Therion printed
        Assert.False(result.Data.Truncated);
    }

    [Fact]
    public async Task A_short_build_is_not_reported_as_truncated()
    {
        using var fixture = FixtureWorkspace.Create();
        var compiler = new StubCompiler { Lines = [Info("one"), Info("two")] };

        var result = await (await LoadedAsync(fixture, compiler)).RunBuild();

        Assert.Equal(2, result.Data!.OutputLines);
        Assert.False(result.Data.Truncated);
        Assert.Equal(["one", "two"], result.Data.Output);
    }

    /// <summary>Cancelling must stop the compiler and surface as cancellation, not a fabricated result.</summary>
    [Fact]
    public async Task Cancellation_reaches_the_compiler_and_is_not_swallowed()
    {
        using var fixture = FixtureWorkspace.Create();
        var compiler = new StubCompiler { BlockUntilCancelled = true };
        var tools = await LoadedAsync(fixture, compiler);
        using var cts = new CancellationTokenSource();

        var running = tools.RunBuild(ct: cts.Token);
        await compiler.Started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(compiler.SawCancellation);
    }

    [Fact]
    public async Task An_explicit_entry_point_is_jailed_and_must_exist()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedAsync(fixture, new StubCompiler());

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace,
            (await tools.RunBuild(entryPoint: "../../elsewhere.thconfig")).Error!.Code);
        Assert.Equal(ToolErrorCodes.FileNotFound,
            (await tools.RunBuild(entryPoint: "nope.thconfig")).Error!.Code);
    }

    [Fact]
    public async Task An_explicit_entry_point_is_compiled_instead_of_the_loaded_one()
    {
        using var fixture = FixtureWorkspace.Create();
        var compiler = new StubCompiler();
        var tools = await LoadedAsync(fixture, compiler);

        var result = await tools.RunBuild(entryPoint: "caves/upper.th");

        Assert.Equal("caves/upper.th", result.Data!.EntryPoint);
        Assert.Equal(fixture.PathTo("caves", "upper.th"), compiler.CompiledPath);
    }

    /// <summary>An artifact Therion wrote outside the workspace keeps its absolute path rather than a bogus relative one.</summary>
    [Fact]
    public async Task An_artifact_outside_the_workspace_keeps_its_absolute_path()
    {
        using var fixture = FixtureWorkspace.Create();
        var outside = Path.Combine(Path.GetTempPath(), "thmcp_elsewhere.lox");
        var compiler = new StubCompiler
        {
            Artifacts = [new OutputArtifact(outside, "model", 1, DateTimeOffset.UnixEpoch)],
        };

        var result = await (await LoadedAsync(fixture, compiler)).RunBuild();

        Assert.Equal(outside, Assert.Single(result.Data!.Artifacts).Path);
    }

    private static CompilerOutputLine Info(string text) => new(text, DiagnosticSeverity.Info, null);
    private static CompilerOutputLine Warning(string text) => new(text, DiagnosticSeverity.Warning, null);
    private static CompilerOutputLine Error(string text) => new(text, DiagnosticSeverity.Error, null);

    private static async Task<BuildTools> LoadedAsync(FixtureWorkspace fixture, ITherionCompiler compiler)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new BuildTools(host, compiler);
    }

    /// <summary>System.Progress posts to the sync context; a test needs the callback on this thread.</summary>
    private sealed class SyncProgress(Action<ProgressNotificationValue> onReport) : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) => onReport(value);
    }

    private sealed class StubCompiler : ITherionCompiler
    {
        public int ExitCode { get; init; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];
        public IReadOnlyList<OutputArtifact> Artifacts { get; init; } = [];
        public IReadOnlyList<CompilerOutputLine> Lines { get; init; } = [];
        public bool BlockUntilCancelled { get; init; }

        public string? CompiledPath { get; private set; }
        public bool SawCancellation { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CompileResult> CompileAsync(
            string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            CompiledPath = entryPointPath;
            Started.TrySetResult();

            foreach (var line in Lines) progress?.Report(line);

            if (BlockUntilCancelled)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { SawCancellation = true; throw; }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new CompileResult(ExitCode, [.. Diagnostics], [.. Artifacts]);
        }
    }
}
