namespace Therion.Blender;

/// <summary>A wall-mesh triangle as three indices into <see cref="CaveScrap.Points"/>.</summary>
public readonly record struct CaveTriangle(uint A, uint B, uint C);

/// <summary>
/// A wall mesh piece from a <c>.lox</c> file (Therion exports both drawn-scrap walls
/// and LRUD-synthesized tube walls as "scrap" chunks). This is the only Therion output
/// carrying real wall geometry.
/// </summary>
public sealed record CaveScrap
{
    /// <summary>Scrap id.</summary>
    public required uint Id { get; init; }

    /// <summary>Owning survey id.</summary>
    public uint SurveyId { get; init; }

    /// <summary>Vertex positions in world coordinates (metres).</summary>
    public required IReadOnlyList<CaveVector3> Points { get; init; }

    /// <summary>Triangles indexing into <see cref="Points"/>.</summary>
    public required IReadOnlyList<CaveTriangle> Triangles { get; init; }
}
