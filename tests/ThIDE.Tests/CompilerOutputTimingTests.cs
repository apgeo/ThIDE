// The Compiler Output time column measures when Therion printed a line. It used to read the clock
// when the UI thread got round to adding the row, so rows that queued up behind the panel's redraws
// showed the queue ("+559 ms, +448 ms, +445 ms…" for lines printed together). The test holds the
// "UI thread" back while the compiler prints, the way a busy panel does.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Therion.Build;
using Therion.Build.Automation;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class CompilerOutputTimingTests
{
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(200);

    /// <summary>Prints three lines <see cref="Gap"/> apart on a reader thread, as the real compiler does.</summary>
    private sealed class PacedCompiler : ITherionCompiler
    {
        public readonly ManualResetEventSlim Printed = new();
        public ValueTask<CompileResult> CompileAsync(string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
            => new(Task.Run(() =>
            {
                for (int i = 1; i <= 3; i++)
                {
                    if (i > 1) Thread.Sleep(Gap);
                    progress?.Report(new CompilerOutputLine($"line {i}", DiagnosticSeverity.Info, null));
                }
                Printed.Set();
                return new CompileResult(0, ImmutableArray<Diagnostic>.Empty, ImmutableArray<OutputArtifact>.Empty);
            }));
    }

    /// <summary>A UI thread that runs posted work only when the test lets it.</summary>
    private sealed class HeldContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback d, object? state) { lock (_queue) _queue.Enqueue((d, state)); }
        public void RunPending()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_queue) { if (_queue.Count == 0) return; item = _queue.Dequeue(); }
                item.Callback(item.State);
            }
        }
    }

    private sealed class NoShell : IShellOpener
    {
        public bool Open(string path) => true;
        public bool RevealInFileManager(string path) => true;
    }

    private sealed class NoTools : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) => new((ToolInfo?)null);
    }

    private sealed class NoCache : IOutputArtifactCache
    {
        public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion) => ImmutableArray<OutputArtifact>.Empty;
        public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts) { }
        public void Clear(string entryPointPath, string therionVersion) { }
    }

    [Fact]
    public void Each_line_is_timed_when_it_was_printed_not_when_its_row_was_added()
    {
        using var dir = new TempDir();
        var cfg = dir.Write("demo.thconfig", "source demo.th\n");
        var compiler = new PacedCompiler();
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Documents" => new ObservableCollection<FileDocumentViewModel>(),
            "get_CurrentDiagnostics" => ImmutableArray<Diagnostic>.Empty,
            _ => null,
        });
        var vm = new BuildViewModel(compiler, new CompileGate(), new NoShell(), new NoTools(), docs, new NoCache());

        var ui = new HeldContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var build = vm.RunBuildAsync(cfg);
            Assert.True(compiler.Printed.Wait(TimeSpan.FromSeconds(10)));
            Thread.Sleep(300);                      // the panel is still busy redrawing
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!build.IsCompleted && DateTime.UtcNow < deadline) { ui.RunPending(); Thread.Sleep(5); }
            ui.RunPending();
            Assert.True(build.IsCompleted);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        var lines = vm.Output.Where(r => r.Text.StartsWith("line ", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, lines.Count);
        var slack = TimeSpan.FromMilliseconds(50);
        Assert.True(lines[1].SinceStart - lines[0].SinceStart >= Gap - slack, $"{lines[0].SinceStart} → {lines[1].SinceStart}");
        Assert.True(lines[2].Delta >= Gap - slack, $"delta {lines[2].Delta}");
        // Since-start never goes back, the summary row included.
        var since = vm.Output.Select(r => r.SinceStart).ToList();
        Assert.Equal(since.OrderBy(t => t).ToList(), since);
    }
}
