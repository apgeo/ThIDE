// Grouping by from-station: a surveyor who returns to a structural station after measuring from
// another one produces two separate runs from the same station. Consumers keep per-batch state
// keyed by StructuralBatch.Key, so each run needs its own key.

using System.Linq;
using Therion.Structural;
using static Therion.Structural.Tests.TestModel;

namespace Therion.Structural.Tests;

public class ReturningStationBatchTests
{
    private static readonly Vec3 P1 = new(1, 0, 0);
    private static readonly Vec3 P2 = new(0, 1, 0.2);
    private static readonly Vec3 P3 = new(1, 1, 0.1);

    [Fact]
    public void Two_runs_from_the_same_station_get_distinct_batch_keys()
    {
        var model = Model(new[]
        {
            Shot("geo1", "p1", P1), Shot("geo1", "p2", P2), Shot("geo1", "p3", P3),
            Shot("geo2", "q1", P1), Shot("geo2", "q2", P2), Shot("geo2", "q3", P3),
            Shot("geo1", "r1", P1), Shot("geo1", "r2", P2), Shot("geo1", "r3", P3),
        });

        var batches = GeoStructureDetector.Detect(model, new DetectionOptions());

        Assert.Equal(3, batches.Length);
        Assert.True(batches.Select(b => b.Key).Distinct().Count() == batches.Length,
            "every batch must have its own key; got: " + string.Join(", ", batches.Select(b => b.Key)));
    }
}
