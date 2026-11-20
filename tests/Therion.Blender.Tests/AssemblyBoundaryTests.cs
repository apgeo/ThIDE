// The four Blender-module assemblies are layered so that a consumer can take the model readers or
// the geometry without the part that starts Blender: Therion.Cave3D ← Therion.CaveGeometry ←
// Therion.Blender ← Therion.Blender.Execution. These tests keep the layers and keep process
// starting in Execution alone.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Therion.Blender.Execution;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class AssemblyBoundaryTests
{
    private static readonly Assembly Cave3D = typeof(CaveModel).Assembly;
    private static readonly Assembly CaveGeometry = typeof(GeometryStage).Assembly;
    private static readonly Assembly Blender = typeof(SceneSpec).Assembly;
    private static readonly Assembly Execution = typeof(BlenderRunner).Assembly;

    private static string[] TherionReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Therion.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The full names of the types an assembly refers to from other assemblies.</summary>
    private static HashSet<string> ReferencedTypes(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(handle);
            names.Add(reader.GetString(type.Namespace) + "." + reader.GetString(type.Name));
        }
        return names;
    }

    [Fact]
    public void The_assemblies_are_the_four_expected_ones()
    {
        Assert.Equal("Therion.Cave3D", Cave3D.GetName().Name);
        Assert.Equal("Therion.CaveGeometry", CaveGeometry.GetName().Name);
        Assert.Equal("Therion.Blender", Blender.GetName().Name);
        Assert.Equal("Therion.Blender.Execution", Execution.GetName().Name);
    }

    [Fact]
    public void Each_layer_references_only_the_layers_below_it()
    {
        Assert.Empty(TherionReferences(Cave3D));
        Assert.Equal(["Therion.Cave3D"], TherionReferences(CaveGeometry));
        Assert.Equal(["Therion.Cave3D", "Therion.CaveGeometry"], TherionReferences(Blender));
        Assert.DoesNotContain("Therion.Blender.Execution", TherionReferences(Blender));
    }

    [Theory]
    [InlineData("Therion.Cave3D")]
    [InlineData("Therion.CaveGeometry")]
    [InlineData("Therion.Blender")]
    public void Only_execution_starts_a_process(string name)
    {
        var assembly = new[] { Cave3D, CaveGeometry, Blender }.Single(a => a.GetName().Name == name);
        var types = ReferencedTypes(assembly);

        Assert.DoesNotContain("System.Diagnostics.Process", types);
        Assert.DoesNotContain("System.Diagnostics.ProcessStartInfo", types);
    }

    [Fact]
    public void Execution_does_start_one()
        => Assert.Contains("System.Diagnostics.Process", ReferencedTypes(Execution));
}
