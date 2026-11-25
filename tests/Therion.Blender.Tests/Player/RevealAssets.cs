// The invented caves of the reveal tests, converted as the application converts a model for a
// spec with a reveal: written as a model file, then through the conversion pipeline.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests;

internal static partial class RevealFixtures
{
    /// <summary>Writes the model as <c>model.3d</c> (or, without its dates, as <c>model.lox</c>)
    /// in a folder of its own and converts it as the application converts a model for
    /// <paramref name="spec"/>: with the reveal values the spec's reveal asks for.</summary>
    public static ProbeAssets Prepare(string name, CaveModel model, SceneSpec spec, bool asLox = false, bool withDatesBeside = false)
    {
        string dir = Path.Combine(SceneProbeRunner.Root, "assets-" + name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, asLox ? "model.lox" : "model.3d");
        if (asLox) LoxWriter.WriteFile(model, path);
        else File.WriteAllBytes(path, As3d(model));
        if (asLox && withDatesBeside) File.WriteAllBytes(Path.Combine(dir, "model.3d"), As3d(model));

        var source = new ResolvedModelSource
        {
            Path = path, Format = asLox ? CaveSourceFormat.Lox : CaveSourceFormat.Survex3d, Kind = ModelSourceKind.ExternalFile,
        };
        var conversion = CaveConversionPipeline.ConvertResolvedFull(source, new ConversionOptions
        {
            OutputDirectory = dir, Leads = InventedCave.Leads(model), Geometry = RevealPlanner.GeometryFor(spec, null, path),
        });
        return new ProbeAssets(dir, conversion, CameraFraming.FromGeometry(conversion.Geometry));
    }
}
