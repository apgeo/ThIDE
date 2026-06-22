using System;
using System.IO;
using Therion.Processing.Abstractions;
using TherionProc.Services;

namespace TherionProc.Tests;

/// <summary>A throwaway temp directory that deletes itself on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TherionProcTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Writes a file (creating subdirectories) and returns its full path.</summary>
    public string Write(string relative, string content = "# test\n")
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relative));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best-effort cleanup */ }
    }
}

/// <summary>Sniffer stub: every probe is inconclusive, so only extensions decide candidacy.</summary>
internal sealed class StubSniffer : IThconfigSniffer
{
    private readonly SnifferVerdict _verdict;
    public StubSniffer(SnifferVerdict verdict = SnifferVerdict.Unknown) => _verdict = verdict;
    public SnifferVerdict Probe(string filePath) => _verdict;
}

/// <summary>In-memory settings, so tests can seed/observe persisted state without disk.</summary>
internal sealed class FakeSettings : IAppSettingsService
{
    public FakeSettings(AppSettings? initial = null) => Current = initial ?? AppSettings.Default;
    public AppSettings Current { get; private set; }
    public event EventHandler? Changed;
    public void Save(AppSettings settings) { Current = settings; Changed?.Invoke(this, EventArgs.Empty); }
}
