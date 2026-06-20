// M5b — Therion.Build smoke tests.

using System.Collections.Immutable;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Build.Tests;

public class OutputParserTests
{
    private readonly HeuristicTherionOutputParser _p = new();

    [Fact]
    public void Detects_file_line_prefix()
    {
        var line = _p.Classify("cave.th:42: error: bogus", isStderr: true);
        Assert.Equal(DiagnosticSeverity.Error, line.Severity);
        Assert.NotNull(line.Span);
        Assert.Equal(42, line.Span!.Value.Start.Line);
        Assert.Equal("cave.th", line.Span.Value.FilePath);
    }

    [Fact]
    public void Warning_keyword_marks_severity()
    {
        var line = _p.Classify("survey.th2:7: warning: missing endscrap", false);
        Assert.Equal(DiagnosticSeverity.Warning, line.Severity);
    }

    [Fact]
    public void Plain_stdout_defaults_to_info()
    {
        var line = _p.Classify("Building...", false);
        Assert.Equal(DiagnosticSeverity.Info, line.Severity);
        Assert.Null(line.Span);
    }
}

public class OutputArtifactCollectorTests
{
    [Fact]
    public void Collects_known_extensions_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "cave.lox"), "x");
        File.WriteAllText(Path.Combine(dir, "cave.3d"), "x");
        File.WriteAllText(Path.Combine(dir, "ignore.tmp"), "x");

        var col = new OutputArtifactCollector();
        var artifacts = col.Collect(dir);

        Assert.Equal(2, artifacts.Length);
        Assert.Contains(artifacts, a => a.Kind == "Loch 3D model");
        Assert.Contains(artifacts, a => a.Kind == "Survex 3D model");

        Directory.Delete(dir, recursive: true);
    }
}

public class TherionCompilerTests
{
    private sealed class NoToolLocator : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default)
            => new((ToolInfo?)null);
    }

    [Fact]
    public async Task Reports_TH_BUILD_001_when_therion_missing()
    {
        var compiler = new TherionCompiler(new NoToolLocator());
        var dir = Path.GetTempPath();
        var entry = Path.Combine(dir, "thconfig");
        await File.WriteAllTextAsync(entry, "source x.th\n");

        var result = await compiler.CompileAsync(entry);

        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(result.Diagnostics, d => d.Code.Value == "TH_BUILD_001");
    }
}
