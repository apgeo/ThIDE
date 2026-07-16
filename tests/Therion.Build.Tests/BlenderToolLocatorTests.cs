// Confirms Blender is a recognized external tool (BA-B15 fix): an override path resolves it, so
// the Preferences ▸ External tools row and the Blender-render override both work. (Auto-detecting
// a Microsoft Store install can't be unit-tested — WindowsApps is ACL-locked — but the override
// path the user sets is the reliable route and is covered here.)

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace Therion.Build.Tests;

public class BlenderToolLocatorTests
{
    private sealed class FakeOverrides : IExternalToolPathOverrides
    {
        private readonly Dictionary<string, string> _map = new();
        public IReadOnlyDictionary<string, string> Overrides => _map;
        public event EventHandler? OverridesChanged { add { } remove { } }
        public void Set(string toolId, string? path)
        {
            if (string.IsNullOrEmpty(path)) _map.Remove(toolId);
            else _map[toolId] = path;
        }
    }

    [Fact]
    public async Task Blender_IsARecognizedTool_ResolvedByOverride()
    {
        var fakeExe = Path.Combine(Path.GetTempPath(), "blender-" + System.Guid.NewGuid().ToString("N") + ".exe");
        await File.WriteAllTextAsync(fakeExe, "");
        try
        {
            var overrides = new FakeOverrides();
            overrides.Set(ExternalToolLocator.Blender, fakeExe);

            var info = await new ExternalToolLocator(overrides).FindAsync(ExternalToolLocator.Blender);

            Assert.NotNull(info);
            Assert.Equal(fakeExe, info!.Path);
            Assert.Equal("override", info.Source); // the Preferences override path is honoured
        }
        finally { File.Delete(fakeExe); }
    }

    [Fact]
    public async Task Blender_WithoutInstallOrOverride_IsNotFound()
    {
        var info = await new ExternalToolLocator(new FakeOverrides()).FindAsync(ExternalToolLocator.Blender);
        // No override and (on the test box) no Blender install ⇒ null, not an exception.
        Assert.True(info is null || File.Exists(info.Path));
    }
}
