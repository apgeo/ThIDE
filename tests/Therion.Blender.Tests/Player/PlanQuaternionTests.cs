// The turn of a camera as a quaternion: what FromView makes of a view, and where the turn points
// the camera. In Blender, PlayerBlenderTests shows the player turning the camera so.

using Therion.Blender.Plan;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlanQuaternionTests
{
    private static void AssertNear(CaveVector3 expected, CaveVector3 actual, double tolerance = 1e-12)
        => Assert.True((expected - actual).Length <= tolerance, $"expected {expected}, was {actual}");

    /// <summary>A view by its heading (clockwise from north), its pitch (up from level) and its
    /// roll (the camera turned clockwise about its line of sight), in degrees, as forward and up directions.</summary>
    internal static (CaveVector3 Forward, CaveVector3 Up) View(double heading, double pitch, double roll)
    {
        double h = heading * Math.PI / 180, p = pitch * Math.PI / 180, r = roll * Math.PI / 180;
        var forward = new CaveVector3(Math.Cos(p) * Math.Sin(h), Math.Cos(p) * Math.Cos(h), Math.Sin(p));
        var level = new CaveVector3(-Math.Sin(p) * Math.Sin(h), -Math.Sin(p) * Math.Cos(h), Math.Cos(p));
        var right = forward.Cross(level);
        return (forward, level * Math.Cos(r) + right * Math.Sin(r));
    }

    [Fact]
    public void NoTurn_LooksStraightDown_WithNorthUp()
    {
        Assert.Equal(PlanQuaternion.Identity, PlanQuaternion.FromView(new CaveVector3(0, 0, -1), new CaveVector3(0, 1, 0)));
        AssertNear(new CaveVector3(0, 0, -1), PlanQuaternion.Identity.Forward);
        AssertNear(new CaveVector3(0, 1, 0), PlanQuaternion.Identity.Up);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(90, 0, 0)]
    [InlineData(200, -30, 0)]
    [InlineData(-45, 60, 15)]
    [InlineData(0, -90, 0)]
    [InlineData(135, -90, 0)]
    [InlineData(270, -90, -20)]
    [InlineData(30, 90, 0)]
    [InlineData(180, 0, 180)]
    [InlineData(10, -89.99, 5)]
    public void AView_IsTheTurnThatPointsTheCameraSo(double heading, double pitch, double roll)
    {
        var (forward, up) = View(heading, pitch, roll);

        var turn = PlanQuaternion.FromView(forward * 3, up * 0.5 + forward * 2);

        Assert.Equal(1.0, turn.Length, 12);
        Assert.True(turn.W >= 0);
        AssertNear(forward, turn.Forward);
        AssertNear(up, turn.Up);
    }

    [Fact]
    public void AViewWithoutADirection_OrWithItsTopAlongTheLineOfSight_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => PlanQuaternion.FromView(CaveVector3.Zero, CaveVector3.UnitZ));
        Assert.Throws<ArgumentException>(() => PlanQuaternion.FromView(new CaveVector3(0, 0, -1), CaveVector3.UnitZ));
        Assert.Throws<ArgumentException>(() => PlanQuaternion.FromView(new CaveVector3(double.NaN, 0, -1), CaveVector3.UnitZ));
    }
}
