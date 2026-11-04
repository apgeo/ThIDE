using Therion.Processing.Abstractions;

namespace Therion.Build.Tests;

public class ExternalToolLocatorProbeTests
{
    private sealed class FakeOverrides(string toolId, string path) : IExternalToolPathOverrides
    {
        public IReadOnlyDictionary<string, string> Overrides { get; } = new Dictionary<string, string> { [toolId] = path };
        public event EventHandler? OverridesChanged { add { } remove { } }
        public void Set(string toolId, string? path) { }
    }

    // A fake `therion` that, like the real one, writes therion.log into its working directory.
    [Fact]
    public async Task VersionProbe_DoesNotWriteTherionLogIntoTheCurrentDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "probe-" + Guid.NewGuid().ToString("N"));
        var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var tool = Path.Combine(root, "therion");
        File.WriteAllText(tool, "#!/bin/sh\necho 'Therion 6.3.4'\necho 'banner' > therion.log\n");
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var log = Path.Combine(project, "therion.log");
        File.WriteAllText(log, "last compile");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(project);
            var info = await new ExternalToolLocator(new FakeOverrides(ExternalToolLocator.Therion, tool))
                .FindAsync(ExternalToolLocator.Therion);

            Assert.Equal("6.3.4", info?.Version);
            Assert.Equal("last compile", File.ReadAllText(log));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(root, recursive: true);
        }
    }
}
