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
    public void SceneSpec_DefaultsToCurrentSchemaVersion()
    {
        var spec = new SceneSpec();
        Assert.Equal(BlenderModule.SceneSpecSchemaVersion, spec.Version);
    }

    [Fact]
    public void RenderResult_DefaultsToEmptyOutputs()
    {
        var result = new RenderResult();

        Assert.False(result.Succeeded);
        Assert.Empty(result.OutputPaths);
    }

    [Fact]
    public void CaveModel_DefaultsToEmptyCollections()
    {
        var model = new CaveModel { SourcePath = "cave.lox", SourceFormat = CaveSourceFormat.Lox };

        Assert.Equal("cave.lox", model.SourcePath);
        Assert.Equal(CaveSourceFormat.Lox, model.SourceFormat);
        Assert.Empty(model.Stations);
        Assert.Empty(model.Shots);
        Assert.False(model.HasWalls);
    }
}
