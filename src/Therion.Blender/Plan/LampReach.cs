// A lamp that reaches as far as the camera looks. The light of a lamp that rides the camera
// falls off with the square of the distance: 80 W light a wall three metres away well and one
// thirty metres away hardly at all. A camera that stands back from a cave therefore sees it
// dark. The reach turns the lamp up by the square of the distance from the camera to what it
// looks at, so that the cave is lit from far as it is from near; nearer than the reference
// distance the lamp keeps the energy it has.
//
// It is worked out here, from the poses of the plan, and written into the plan as keys of the
// lamp's energy (a change): the player holds no rule for it, and a plan says what it does.

using Therion.Cave3D;

namespace Therion.Blender.Plan;

/// <summary>Turns the lamp on the camera up with the distance the camera looks over.</summary>
public static class LampReach
{
    /// <summary>The distance, in metres, a lamp lights as its energy says: a wall this far
    /// from the 80 W head lamp of the look this library gives a cave is lit to about the middle
    /// of what a picture can show.</summary>
    public const double DefaultDistance = 3.0;

    /// <summary>How far, as a share of its value, the energy between two keys may be from the
    /// energy the distance asks for there. A fiftieth of a lamp's energy is not seen.</summary>
    public const double DefaultTolerance = 0.02;

    /// <summary>The energy of a lamp of <paramref name="energy"/> that is to light something
    /// <paramref name="distance"/> metres away as it lights something
    /// <paramref name="reference"/> metres away: more by the square of the distance, and never
    /// less than it has.</summary>
    public static double Energy(double energy, double distance, double reference = DefaultDistance)
    {
        if (!(reference > 0) || !double.IsFinite(reference))
            throw new ArgumentOutOfRangeException(nameof(reference), reference, "The reference distance is above 0.");
        if (!(distance > reference) || !double.IsFinite(distance)) return energy;
        double times = distance / reference;
        return energy * times * times;
    }

    /// <summary>
    /// The keys of a lamp's energy for a camera that looks over the given distances: at each
    /// time the energy <see cref="Energy"/> gives, with the keys left out that a straight line
    /// between their neighbours stands in for (within <paramref name="tolerance"/>).
    /// </summary>
    /// <param name="distances">How far the camera looks at each time, in rising order of time:
    /// the distance to what the lamp is to light.</param>
    public static IReadOnlyList<PlanNumberKey> Keys(
        IReadOnlyList<(double T, double Distance)> distances, double energy,
        double reference = DefaultDistance, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(distances);
        if (!(energy >= 0) || !double.IsFinite(energy))
            throw new ArgumentOutOfRangeException(nameof(energy), energy, "A lamp's energy is not negative.");
        if (!(tolerance >= 0)) throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance is not negative.");
        var all = distances.Select(d => (d.T, Value: Rounded(Energy(energy, d.Distance, reference)))).ToList();
        if (all.Count == 0) return [];

        // From each key that is kept, the farthest key that a straight line reaches with every
        // key between them within the tolerance.
        var kept = new List<PlanNumberKey> { new(all[0].T, all[0].Value) };
        int from = 0;
        while (from < all.Count - 1)
        {
            int to = from + 1;
            for (int next = from + 2; next < all.Count; next++)
            {
                if (!Straight(all, from, next, tolerance)) break;
                to = next;
            }
            kept.Add(new PlanNumberKey(all[to].T, all[to].Value));
            from = to;
        }
        // A lamp that never changes has one key.
        return kept.All(k => k.Value == kept[0].Value) ? [kept[0]] : kept;
    }

    private static bool Straight(List<(double T, double Value)> all, int from, int to, double tolerance)
    {
        double span = all[to].T - all[from].T;
        if (!(span > 0)) return false;
        for (int i = from + 1; i < to; i++)
        {
            double share = (all[i].T - all[from].T) / span;
            double line = all[from].Value + (all[to].Value - all[from].Value) * share;
            if (Math.Abs(line - all[i].Value) > tolerance * Math.Max(all[i].Value, 1e-9)) return false;
        }
        return true;
    }

    /// <summary>
    /// How far the camera of <paramref name="camera"/> looks at every frame of
    /// <paramref name="timeline"/>: from where it stands to its target, both taken straight from
    /// pose to pose (or held, for a camera that jumps). A camera that eases between its poses
    /// (<see cref="PlanInterpolation.Bezier"/>) is a little off the straight way between them;
    /// a lamp's energy does not show it.
    /// </summary>
    public static IReadOnlyList<(double T, double Distance)> Distances(PlanCamera camera, PlanTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(timeline);
        var track = camera.Track;
        if (track.Count == 0) return [];
        var distances = new List<(double, double)>(timeline.FrameCount);
        int at = 0;
        for (int frame = 1; frame <= timeline.FrameCount; frame++)
        {
            double t = (frame - 1) / (double)timeline.Fps;
            while (at + 1 < track.Count && track[at + 1].T <= t) at++;
            CaveVector3 eye = track[at].Eye, target = track[at].Target;
            if (at + 1 < track.Count && t > track[at].T && camera.Interpolation != PlanInterpolation.Constant)
            {
                double share = (t - track[at].T) / (track[at + 1].T - track[at].T);
                eye += (track[at + 1].Eye - eye) * share;
                target += (track[at + 1].Target - target) * share;
            }
            distances.Add((Math.Round(t, 6), (target - eye).Length));
        }
        return distances;
    }

    /// <summary>
    /// <paramref name="plan"/> with the lamp on its camera reaching as far as the camera looks:
    /// the lamp's energy is keyed by the distance from each pose to its target
    /// (<see cref="Distances"/>, <see cref="Keys"/>). A lamp whose energy comes out the same
    /// throughout (a camera that stands still) is given that energy and no keys. A plan without
    /// a head lamp is returned as it is.
    /// </summary>
    /// <param name="lightId">The lamp, by its id; null takes every light of the kind
    /// <see cref="PlanLightKind.Headlamp"/>.</param>
    /// <param name="distances">How far the camera looks at each time, when whoever makes the
    /// plan knows it better than the poses' targets say (the distance to the nearest cave, for
    /// a camera whose target is only where it focuses); null takes the poses' targets.</param>
    /// <exception cref="ArgumentException">The plan has no light <paramref name="lightId"/>, or
    /// a change keys the lamp's energy already.</exception>
    public static ScenePlan Apply(
        ScenePlan plan, double reference = DefaultDistance, string? lightId = null,
        IReadOnlyList<(double T, double Distance)>? distances = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var lamps = plan.Look.Lights
            .Where(l => lightId is null ? l.Kind == PlanLightKind.Headlamp : l.Id == lightId).ToList();
        if (lamps.Count == 0)
        {
            if (lightId is not null) throw new ArgumentException($"The plan has no light with the id '{lightId}'.", nameof(lightId));
            return plan;
        }
        distances ??= Distances(plan.Camera, plan.Timeline);
        var lights = plan.Look.Lights.ToList();
        var changes = (plan.Changes ?? []).ToList();
        foreach (var lamp in lamps)
        {
            if (changes.Any(c => c is { Energy: not null, Target: not null }
                                 && (c.Target.Id == lamp.Id || c.Target.Role == "light")))
                throw new ArgumentException($"A change keys the energy of '{lamp.Id}' already; a lamp's reach is its energy, keyed.", nameof(plan));
            var keys = Keys(distances, lamp.Energy, reference);
            if (keys.Count == 0) continue;
            if (keys.Count == 1)
                lights[lights.IndexOf(lamp)] = lamp with { Energy = keys[0].Value };
            else
                changes.Add(new PlanChange { Target = new PlanChangeTarget { Id = lamp.Id }, Energy = keys });
        }
        return ScenePlanBuilder.WithNeededFeatures(plan with
        {
            Look = plan.Look with { Lights = lights },
            Changes = changes.Count == 0 ? plan.Changes : changes,
        });
    }

    private static double Rounded(double value) => Math.Round(value, 6);
}
