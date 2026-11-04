using Therion.Processing.Abstractions;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

/// <summary>Settings ▸ External Tools: the Test button checks the override the user typed.</summary>
public class ExternalToolRowTests
{
    private sealed class Locator(ToolInfo? found) : IExternalToolLocator
    {
        public int Calls { get; private set; }
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(found);
        }
    }

    private sealed class Overrides : IExternalToolPathOverrides
    {
        private readonly Dictionary<string, string> _map = new();
        IReadOnlyDictionary<string, string> IExternalToolPathOverrides.Overrides => _map;
        public event EventHandler? OverridesChanged { add { } remove { } }
        public void Set(string toolId, string? path) { if (string.IsNullOrEmpty(path)) _map.Remove(toolId); else _map[toolId] = path; }
    }

    [Fact]
    public async Task Test_WithAnOverrideThatDoesNotExist_ReportsItInsteadOfTheInstalledTool()
    {
        var installed = new ToolInfo("blender", "/usr/bin/blender", "5.0.1", "PATH");
        var locator = new Locator(installed);
        var row = new ExternalToolRow("blender", new Overrides(), locator);
        var missing = Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "blender");
        row.OverridePath = missing;

        await row.TestCommand.ExecuteAsync(null);

        Assert.Contains(missing, row.TestResult);
        Assert.DoesNotContain("OK", row.TestResult);
        Assert.Equal(0, locator.Calls);
    }

    [Fact]
    public async Task Test_WithoutAnOverride_ReportsTheDetectedTool()
    {
        var row = new ExternalToolRow("blender", new Overrides(), new Locator(new ToolInfo("blender", "/usr/bin/blender", "5.0.1", "PATH")));

        await row.TestCommand.ExecuteAsync(null);

        Assert.Equal("OK (5.0.1)", row.TestResult);
    }
}
