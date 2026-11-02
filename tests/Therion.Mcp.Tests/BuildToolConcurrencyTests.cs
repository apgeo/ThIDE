using Therion.Mcp.Tools;
using Therion.Processing.Abstractions;

namespace Therion.Mcp.Tests;

/// <summary>
/// One compile at a time per workspace: two Therion runs on the same thconfig write the same outputs
/// and the same log. run_build must hold that rule itself, as nothing upstream of it does.
/// </summary>
public class BuildToolConcurrencyTests
{
    [Fact]
    public async Task Two_parallel_run_build_calls_never_compile_at_the_same_time()
    {
        using var fixture = FixtureWorkspace.Create();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var compiler = new OverlapCountingCompiler(expectedStarts: 2);
        var tools = new BuildTools(host, compiler);

        var first = tools.RunBuild();
        var second = tools.RunBuild();

        // Give both calls a chance to reach the compiler; a serialised tool never lets the second in
        // while the first runs, so the wait times out and the first is released alone.
        await Task.WhenAny(compiler.AllStarted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        compiler.Release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, compiler.MaxConcurrent);
        await host.DisposeAsync();
    }

    private sealed class OverlapCountingCompiler(int expectedStarts) : ITherionCompiler
    {
        private int _active;
        private int _started;

        public int MaxConcurrent { get; private set; }
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<CompileResult> CompileAsync(
            string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            int now = Interlocked.Increment(ref _active);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, now);
            if (Interlocked.Increment(ref _started) == expectedStarts) AllStarted.TrySetResult();

            await Release.Task;

            Interlocked.Decrement(ref _active);
            return new CompileResult(0, [], []);
        }
    }
}
