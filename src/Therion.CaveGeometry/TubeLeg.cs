// The cross-sections the tube mesher gives a leg, kept beside the mesh so that code which needs
// to know where the passage is (the flythrough camera) reads the same numbers the walls were
// built from, instead of guessing them back from wall vertices.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

/// <summary>
/// The cross-section of a synthesized tube at one end of a leg: an oval through the four wall
/// distances, in the plane spanned by <see cref="RightAxis"/> and <see cref="UpAxis"/> around the
/// station. The station is rarely in the middle of it — the middle is <see cref="Center"/>.
/// </summary>
/// <param name="Station">The station the distances were measured from.</param>
/// <param name="RightAxis">Unit vector toward the right wall (horizontal, across the leg).</param>
/// <param name="UpAxis">Unit vector toward the roof, at right angles to the leg and to
/// <paramref name="RightAxis"/>.</param>
/// <param name="Left">Distance to the wall along −<paramref name="RightAxis"/>, metres.</param>
/// <param name="Right">Distance to the wall along <paramref name="RightAxis"/>, metres.</param>
/// <param name="Up">Distance to the roof along <paramref name="UpAxis"/>, metres.</param>
/// <param name="Down">Distance to the floor along −<paramref name="UpAxis"/>, metres.</param>
public readonly record struct TubeSection(
    CaveVector3 Station, CaveVector3 RightAxis, CaveVector3 UpAxis,
    double Left, double Right, double Up, double Down)
{
    /// <summary>The middle of the cross-section: halfway between the left and right walls and
    /// between the floor and the roof.</summary>
    public CaveVector3 Center => Station + RightAxis * ((Right - Left) * 0.5) + UpAxis * ((Up - Down) * 0.5);

    /// <summary>Half the distance between the left and right walls.</summary>
    public double HalfWidth => (Left + Right) * 0.5;

    /// <summary>Half the distance between the floor and the roof.</summary>
    public double HalfHeight => (Up + Down) * 0.5;

    /// <summary>Where the outline is at angle <paramref name="theta"/> (radians, 0 at the right
    /// wall, a quarter turn at the roof), as offsets from the station along the two axes. Each
    /// quarter is a quarter of an ellipse through its two wall distances.</summary>
    internal (double Across, double Along) OutlineAt(double theta)
    {
        double cos = Math.Cos(theta), sin = Math.Sin(theta);
        double horizontal = cos >= 0 ? Right * cos : Left * cos; // cos<0 → toward left wall
        double vertical = sin >= 0 ? Up * sin : Down * sin;      // sin<0 → toward floor
        return (horizontal, vertical);
    }
}

/// <summary>
/// One leg as the tube mesher skins it: the cross-sections at its two ends, which share the leg's
/// own pair of axes, joined by straight walls between the corners of a polygon with
/// <see cref="Sides"/> sides. Where legs meet, the mesher ends those walls on the join with the
/// other tubes instead of on the cross-section at the station: a little before it on the inside
/// of a bend, a little beyond it on the outside.
/// </summary>
public readonly record struct TubeLeg(TubeSection From, TubeSection To, int Sides)
{
    /// <summary>The same tube seen from its other end. The axes stay as they are, so left is
    /// still the mesher's left, not the left of somebody walking the other way.</summary>
    public TubeLeg Reversed() => new(To, From, Sides);

    /// <summary>The cross-section a fraction <paramref name="t"/> of the way along the leg
    /// (0 at <see cref="From"/>, 1 at <see cref="To"/>).</summary>
    public TubeSection SectionAt(double t) => new(
        From.Station.Lerp(To.Station, t), From.RightAxis, From.UpAxis,
        From.Left + (To.Left - From.Left) * t, From.Right + (To.Right - From.Right) * t,
        From.Up + (To.Up - From.Up) * t, From.Down + (To.Down - From.Down) * t);

    /// <summary>
    /// How much room <paramref name="point"/> has inside the tube: its distance to the nearest
    /// side wall, measured in the cross-section it lies in. Negative when the point is outside
    /// that cross-section. The end caps are not walls here, and a point beyond either end is
    /// measured against the cross-section of that end.
    /// </summary>
    public double Clearance(CaveVector3 point)
    {
        var axis = To.Station - From.Station;
        double lengthSquared = axis.LengthSquared;
        double t = lengthSquared < 1e-18 ? 0.0 : Math.Clamp((point - From.Station).Dot(axis) / lengthSquared, 0.0, 1.0);
        var section = SectionAt(t);
        var offset = point - section.Station;
        double x = offset.Dot(section.RightAxis), y = offset.Dot(section.UpAxis);

        // Signed distance to the polygon the mesher builds (convex, counter-clockwise).
        int sides = Math.Max(3, Sides);
        double nearest = double.PositiveInfinity;
        bool inside = true;
        var (ax, ay) = section.OutlineAt(0.0);
        for (int s = 1; s <= sides; s++)
        {
            var (bx, by) = section.OutlineAt(2.0 * Math.PI * (s % sides) / sides);
            double ex = bx - ax, ey = by - ay;
            double px = x - ax, py = y - ay;
            if (ex * py - ey * px < 0.0) inside = false;
            double edgeSquared = ex * ex + ey * ey;
            double along = edgeSquared < 1e-18 ? 0.0 : Math.Clamp((px * ex + py * ey) / edgeSquared, 0.0, 1.0);
            double dx = px - ex * along, dy = py - ey * along;
            nearest = Math.Min(nearest, Math.Sqrt(dx * dx + dy * dy));
            (ax, ay) = (bx, by);
        }
        return inside ? nearest : -nearest;
    }
}
