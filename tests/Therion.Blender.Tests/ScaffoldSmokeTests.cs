// BA-B1 scaffold smoke tests — prove the Therion.Blender library is wired into the build
// and its public seam types are reachable + instantiable. Real behavior is tested batch by
// batch (parser BA-B2, geometry/writers BA-B3, emitter BA-B5, runner BA-B10). These exist
// so the test project compiles and runs green from day one; replace/extend, don't rely on
// them as the module grows.

using Therion.Blender;

namespace Therion.Blender.Tests;

public class ScaffoldSmokeTests
{
    [Fact]
    public void SchemaVersions_ArePositive()
    {
        Assert.True(BlenderModule.SceneMetaSchemaVersion > 0);
        Assert.True(BlenderModule.SceneSpecSchemaVersion > 0);
    }

    [Fact]
    public void SceneSpec_DefaultsToCurrentSchemaVersion()
    {
        var spec = new SceneSpec();
        Assert.Equal(BlenderModule.SceneSpecSchemaVersion, spec.Version);
    }

    [Fact]
    public void RenderProgress_CarriesPhaseAndOptionalNumericFields()
    {
        var tick = new RenderProgress(RenderPhase.Rendering, "Rendering frame", Fraction: 0.5, Frame: 3, FrameCount: 10);

        Assert.Equal(RenderPhase.Rendering, tick.Phase);
        Assert.Equal(0.5, tick.Fraction);
        Assert.Equal(3, tick.Frame);
        Assert.Equal(10, tick.FrameCount);
        Assert.Null(tick.SampleFraction); // unset optional stays null (tier-2 has no sample data)
    }

    [Fact]
    public void RenderResult_DefaultsToEmptyOutputs()
    {
        var result = new RenderResult();

        Assert.False(result.Succeeded);
        Assert.Empty(result.OutputPaths);
    }

    [Fact]
    public void CaveModel_ScaffoldContainerConstructs()
    {
        var model = new CaveModel { SourcePath = "cave.lox", SourceFormat = "loch" };

        Assert.Equal("cave.lox", model.SourcePath);
        Assert.Equal("loch", model.SourceFormat);
    }

    [Fact]
    public void IBlenderRenderService_SeamIsImplementable()
    {
        // A do-nothing implementation compiles against the scaffold seam — proves the
        // interface surface is coherent before any real stage exists.
        IBlenderRenderService service = new NoopRenderService();
        Assert.NotNull(service);
    }

    private sealed class NoopRenderService : IBlenderRenderService
    {
        public Task<RenderResult> RenderAsync(SceneSpec spec, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new RenderResult());

        public Task<string> ExportScriptAsync(SceneSpec spec, string outputDir, IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(System.IO.Path.Combine(outputDir, "render.py"));
    }
}
