// Implementation Plan �9bis.5 � Build menu + Compiler Output + Generated Files.
// One VM hosts the build pipeline (Build / Rebuild / Cancel), streams compiler
// output, and exposes the artifact list so the Loch / Aven quick actions can
// light up when matching outputs exist.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public sealed record CompilerOutputRow(string Text, string Severity, Therion.Core.SourceSpan? Span);
public sealed record ArtifactRow(string Path, string Kind, long SizeBytes, DateTimeOffset LastWriteUtc)
{
    public string SizeDisplay => SizeBytes < 1024 ? $"{SizeBytes} B" : $"{SizeBytes / 1024} KB";
}

public partial class BuildViewModel : ViewModelBase
{
    private readonly ITherionCompiler _compiler;
    private readonly ICompileGate _gate;
    private readonly IShellOpener _shell;
    private readonly IExternalToolLocator _locator;
    private readonly IDocumentService _documents;
    private readonly IOutputArtifactCache _artifactCache;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isBuilding;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private IReadOnlyList<CompilerOutputRow> _output = Array.Empty<CompilerOutputRow>();
    [ObservableProperty] private IReadOnlyList<ArtifactRow> _artifacts = Array.Empty<ArtifactRow>();
    [ObservableProperty] private bool _hasLoxArtifact;
    [ObservableProperty] private bool _hasAvenArtifact;
    [ObservableProperty] private CompilerOutputRow? _selectedOutput;

    // ---- build result indicator (#7) ----------------------------------------
    /// <summary>True once any build has completed in this session.</summary>
    [ObservableProperty] private bool _hasBuildResult;
    /// <summary>True when the last completed build had exit code 0.</summary>
    [ObservableProperty] private bool _lastBuildSucceeded;
    /// <summary>True when the last build produced diagnostics with Warning severity.</summary>
    [ObservableProperty] private bool _lastBuildHasWarnings;
    /// <summary>Number of warnings from the last build.</summary>
    [ObservableProperty] private int _lastBuildWarningCount;

    /// <summary>Raised when a compiler-output row with a span is activated.</summary>
    public event System.EventHandler<Therion.Core.SourceSpan>? NavigateRequested;

    [RelayCommand]
    private void NavigateOutput(CompilerOutputRow? row)
    {
        if (row?.Span is { } span && !span.IsEmpty)
            NavigateRequested?.Invoke(this, span);
    }

    private readonly List<CompilerOutputRow> _outputBuffer = new();

    public BuildViewModel(
        ITherionCompiler compiler,
        ICompileGate gate,
        IShellOpener shell,
        IExternalToolLocator locator,
        IDocumentService documents,
        IOutputArtifactCache artifactCache)
    {
        _compiler = compiler;
        _gate = gate;
        _shell = shell;
        _locator = locator;
        _documents = documents;
        _artifactCache = artifactCache;
        _documents.DocumentChanged += (_, _) => RestoreLastArtifacts();
    }

    public BuildViewModel() : this(
        new NullCompiler(), new CompileGate(), new ShellOpener(),
        new NullLocator(), new NullDocumentService(), new NullArtifactCache())
    { } // Designer-only.

    public event EventHandler<ImmutableArray<Diagnostic>>? CompileCompleted;

    public bool CanBuild => !IsBuilding && _documents.CurrentPath is not null;

    [RelayCommand]
    private async Task BuildAsync()
    {
        var entry = _documents.CurrentPath;
        if (entry is null) { Status = "No project loaded."; return; }

        using var lease = _gate.TryAcquire();
        if (lease is null) { Status = "A build is already in progress."; return; }

        IsBuilding = true;
        _outputBuffer.Clear();
        Output = Array.Empty<CompilerOutputRow>();
        _cts = new CancellationTokenSource();

        var progress = new Progress<CompilerOutputLine>(line =>
        {
            _outputBuffer.Add(new CompilerOutputRow(line.Text, line.Severity.ToString(), line.Span));
            // Snapshot for binding (immutable swap, �18).
            Output = _outputBuffer.ToArray();
        });

        try
        {
            var result = await _compiler.CompileAsync(entry, progress, _cts.Token).ConfigureAwait(true);
            UpdateArtifacts(result.Artifacts);
            _artifactCache.Save(entry, "unknown", result.Artifacts);
            var warnCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            HasBuildResult = true;
            LastBuildSucceeded = result.ExitCode == 0;
            LastBuildHasWarnings = warnCount > 0;
            LastBuildWarningCount = warnCount;
            Status = result.ExitCode == 0
                ? $"Build succeeded ({result.Artifacts.Length} artifact(s))."
                : $"Build failed (exit {result.ExitCode}).";
            CompileCompleted?.Invoke(this, result.Diagnostics);
        }
        catch (OperationCanceledException)
        {
            Status = "Build cancelled.";
        }
        catch (Exception ex)
        {
            Status = "Build error: " + ex.Message;
        }
        finally
        {
            IsBuilding = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private Task RebuildAsync() => BuildAsync();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClearOutput()
    {
        _outputBuffer.Clear();
        Output = Array.Empty<CompilerOutputRow>();
    }

    /// <summary>Returns all output rows as plain text for clipboard copy.</summary>
    public string OutputAsText() =>
        string.Join(Environment.NewLine, Output.Select(r => $"{r.Severity,-8} {r.Text}"));

    [RelayCommand]
    private async Task OpenInLochAsync()
    {
        var lox = Artifacts.FirstOrDefault(a => a.Kind.Equals("lox", StringComparison.OrdinalIgnoreCase));
        if (lox is null) return;
        var tool = await _locator.FindAsync(ExternalToolLocator.Loch).ConfigureAwait(true);
        if (tool is not null)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool.Path, $"\"{lox.Path}\"") { UseShellExecute = false }); return; }
            catch { /* fall through to shell-open */ }
        }
        _shell.Open(lox.Path);
    }

    [RelayCommand]
    private async Task OpenInAvenAsync()
    {
        var d3 = Artifacts.FirstOrDefault(a => a.Kind.Equals("3d", StringComparison.OrdinalIgnoreCase));
        if (d3 is null) return;
        var tool = await _locator.FindAsync(ExternalToolLocator.Aven).ConfigureAwait(true);
        if (tool is not null)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool.Path, $"\"{d3.Path}\"") { UseShellExecute = false }); return; }
            catch { /* fall through to shell-open */ }
        }
        _shell.Open(d3.Path);
    }

    [RelayCommand]
    private void OpenLastOutputFolder()
    {
        var first = Artifacts.FirstOrDefault();
        if (first is null) return;
        var dir = Path.GetDirectoryName(first.Path);
        if (!string.IsNullOrEmpty(dir)) _shell.Open(dir);
    }

    [RelayCommand]
    private void OpenArtifact(ArtifactRow? row)
    {
        if (row is null) return;
        _shell.Open(row.Path);
    }

    [RelayCommand]
    private void RevealArtifact(ArtifactRow? row)
    {
        if (row is null) return;
        _shell.RevealInFileManager(row.Path);
    }

    private void UpdateArtifacts(ImmutableArray<OutputArtifact> artifacts)
    {
        var rows = artifacts
            .Select(a => new ArtifactRow(a.Path, a.Kind, a.SizeBytes, a.LastWriteUtc))
            .OrderBy(a => a.Kind, StringComparer.Ordinal)
            .ThenBy(a => a.Path, StringComparer.Ordinal)
            .ToList();
        Artifacts = rows;
        HasLoxArtifact  = rows.Any(a => a.Kind.Equals("lox", StringComparison.OrdinalIgnoreCase));
        HasAvenArtifact = rows.Any(a => a.Kind.Equals("3d",  StringComparison.OrdinalIgnoreCase));
    }

    private void RestoreLastArtifacts()
    {
        var entry = _documents.CurrentPath;
        if (entry is null) { UpdateArtifacts(ImmutableArray<OutputArtifact>.Empty); return; }
        var cached = _artifactCache.Load(entry, "unknown");
        UpdateArtifacts(cached);
    }

    // ---- Designer-only null sinks (kept private � never resolved at runtime) ----

    private sealed class NullCompiler : ITherionCompiler
    {
        public ValueTask<CompileResult> CompileAsync(string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default) =>
            new(new CompileResult(0, ImmutableArray<Diagnostic>.Empty, ImmutableArray<OutputArtifact>.Empty));
    }
    private sealed class NullLocator : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) => new((ToolInfo?)null);
    }
    private sealed class NullArtifactCache : IOutputArtifactCache
    {
        public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion) => ImmutableArray<OutputArtifact>.Empty;
        public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts) { }
        public void Clear(string entryPointPath, string therionVersion) { }
    }
}
