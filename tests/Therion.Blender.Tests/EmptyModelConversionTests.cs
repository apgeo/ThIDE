// An empty model (a 0-byte .lox is documented as valid and empty) must still convert to
// assets on disk: the scene metadata has to be writable as JSON.

using Therion.Blender.Sources;
using Therion.Blender.Writers;

namespace Therion.Blender.Tests;

public class EmptyModelConversionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-empty-" + Guid.NewGuid().ToString("N"));

    public EmptyModelConversionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void EmptyLoxFile_ConvertsWithoutThrowing_AndWritesFiniteBounds()
    {
        string loxPath = Path.Combine(_dir, "empty.lox");
        File.WriteAllBytes(loxPath, []);
        var source = new ResolvedModelSource { Path = loxPath, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        var options = new ConversionOptions { OutputDirectory = Path.Combine(_dir, "out") };

        ConversionResult? result = null;
        var error = Record.Exception(() => result = CaveConversionPipeline.ConvertResolvedFull(source, options));

        Assert.Null(error);
        var meta = SceneMetaWriter.Read(File.ReadAllText(result!.Manifest.SceneMetaPath));
        Assert.True(double.IsFinite(meta.WorldBounds.Min.X), "world bounds must be finite for an empty model");
    }
}
