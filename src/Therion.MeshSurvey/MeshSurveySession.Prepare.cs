// The slow stage: the void and its skeleton. Its result depends only on the input, void and skeleton
// settings (and the start points that seed a point cloud's flood), so it is cached and reused while
// the leg, lateral, coverage and splay settings change.

using Therion.Cave3D;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey;

public sealed partial class MeshSurveySession
{
    /// <summary>The void of the last <see cref="BuildVoid"/>, if any.</summary>
    internal VoidGrid? PreparedVoid { get; private set; }

    /// <summary>Builds the void (internal frame seeds).</summary>
    internal (VoidGrid Grid, VoidReport Report) BuildVoid(MeshSurveyOptions options, IReadOnlyList<CaveVector3> seeds,
        IProgress<MeshSurveyProgress>? progress, CancellationToken ct)
    {
        var result = VoidBuilder.Build(this, options, seeds, progress, ct);
        PreparedVoid = result.Grid;
        return result;
    }
}
