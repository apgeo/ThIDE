using Therion.Cave3D;
using Therion.MeshSurvey.Skeleton;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey;

public sealed partial class MeshSurveySession
{
    /// <summary>Builds the skeleton of a void (internal frame protected points).</summary>
    internal SkeletonResult BuildSkeleton(VoidGrid grid, MeshSurveyOptions options, IReadOnlyList<CaveVector3> protectedPoints,
        IProgress<MeshSurveyProgress>? progress, CancellationToken ct) =>
        SkeletonBuilder.Build(grid, Model, options.Skeleton, protectedPoints, progress, ct);
}
