using Therion.Blender.Geometry;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests.SceneProbe;

public class InventedCaveTests
{
    [Theory]
    [InlineData(InventedCaveSize.Small)]
    [InlineData(InventedCaveSize.Medium)]
    [InlineData(InventedCaveSize.Large)]
    public void SameSizeAndSeed_GiveTheSameFileBytes(InventedCaveSize size)
        => Assert.Equal(LoxWriter.Write(InventedCave.Build(size)), LoxWriter.Write(InventedCave.Build(size)));

    [Fact]
    public void AnotherSeed_GivesAnotherCave()
        => Assert.NotEqual(
            LoxWriter.Write(InventedCave.Build(InventedCaveSize.Medium, seed: 1)),
            LoxWriter.Write(InventedCave.Build(InventedCaveSize.Medium, seed: 2)));

    [Theory]
    [InlineData(InventedCaveSize.Small, 16, 16, 1, 1)]
    [InlineData(InventedCaveSize.Medium, 100, 140, 2, 3)]
    [InlineData(InventedCaveSize.Large, 3_900, 4_100, 2, 0)]
    public void Sizes_HaveTheirShape(InventedCaveSize size, int minLegs, int maxLegs, int entrances, int leads)
    {
        var model = InventedCave.Build(size);

        Assert.InRange(model.Shots.Count, minLegs, maxLegs);
        Assert.Equal(entrances, model.Stations.Count(s => s.IsEntrance));
        Assert.Equal(leads, InventedCave.Leads(model).Count);
        Assert.Equal(model.Stations.Count, model.Stations.Select(s => s.Name).Distinct().Count());
        Assert.True(model.HasWalls);
    }

    [Fact]
    public void Medium_HasALoopAndTwoLevels()
    {
        var model = InventedCave.Build(InventedCaveSize.Medium);

        // A connected network with a loop has at least as many legs as stations.
        Assert.True(model.Shots.Count >= model.Stations.Count, $"{model.Shots.Count} legs, {model.Stations.Count} stations");
        double depth = model.Stations.Max(s => s.Position.Z) - model.Stations.Min(s => s.Position.Z);
        Assert.True(depth > 25.0, $"the levels are only {depth:F1} m apart");
    }

    [Fact]
    public void Large_MakesTensOfThousandsOfWallTriangles()
    {
        var geometry = GeometryStage.Build(InventedCave.Build(InventedCaveSize.Large));

        // Sixteen to a leg once the tubes are joined, more round a junction, eight to a cap.
        Assert.True(geometry.Walls.Triangles.Count > 70_000, $"{geometry.Walls.Triangles.Count} triangles");
        Assert.True(geometry.LocalBounds.Size.X > 2_500.0, "the far cave lies three kilometres east");
    }
}
