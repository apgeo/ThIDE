using System;
using System.IO;
using Therion.Processing.Abstractions;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>A throwaway temp directory that deletes itself on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ThIDETests_" + Guid.NewGuid().ToString("N"));
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

/// <summary>
/// Stand-ins for what the app's design-time constructors would read from the user profile or probe
/// on the machine, so tests that compose the shell never touch either.
/// </summary>
internal static class TestProfile
{
    /// <summary>A path in a fresh, not-yet-created temp folder; nothing appears there unless a test saves.</summary>
    public static string NewPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), "ThIDETests_profile_" + Guid.NewGuid().ToString("N"), fileName);

    public static ThIDE.ViewModels.Docking.SettingsToolViewModel SettingsTool() => new(
        new ThIDE.ViewModels.SettingsViewModel(new NoTools()),
        new ThIDE.ViewModels.KeyboardShortcutsViewModel(new JsonKeyboardShortcutService(NewPath("keyboard.json"))));

    private sealed class NoTools : IExternalToolLocator
    {
        public System.Threading.Tasks.ValueTask<ToolInfo?> FindAsync(string toolId, System.Threading.CancellationToken cancellationToken = default) =>
            new((ToolInfo?)null);
    }
}
