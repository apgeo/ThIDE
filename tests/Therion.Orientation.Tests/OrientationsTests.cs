// Azimuth folding, strike / dip-direction conversion and poles against the textbook values: under
// the right-hand rule a plane striking 030 and dipping 40 dips towards 120, and its pole plunges
// 50 towards 300 (as plotted in Lisle and Leyshon, "Stereographic Projection Techniques for
// Geologists and Civil Engineers").

using System;
using Therion.Orientation;
using Xunit;

namespace Therion.Orientation.Tests;

public class OrientationsTests
{
    [Theory]
    [InlineData(30, 120)]
    [InlineData(0, 90)]
    [InlineData(270, 0)]
    [InlineData(350, 80)]
    [InlineData(360, 90)]
    public void A_right_hand_rule_strike_dips_ninety_degrees_clockwise_of_it(double strike, double dipDirection)
    {
        Assert.Equal(dipDirection, Planes.DipDirectionFromStrike(strike), 1e-9);
        Assert.Equal(Azimuth.Normalize(strike), Planes.StrikeFromDipDirection(dipDirection), 1e-9);
    }

    [Fact]
    public void The_pole_trends_opposite_the_dip_direction_and_plunges_at_the_complement_of_the_dip()
    {
        var pole = Planes.Pole(new PlaneOrientation(120, 40));
        Assert.Equal(300, pole.TrendDegrees, 1e-9);
        Assert.Equal(50, pole.PlungeDegrees, 1e-9);

        // A horizontal bed's pole is vertical; a vertical plane's pole lies in the horizontal.
        Assert.Equal(90, Planes.Pole(new PlaneOrientation(0, 0)).PlungeDegrees);
        Assert.Equal(new LineOrientation(70, 0), Planes.Pole(new PlaneOrientation(250, 90)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Planes.Pole(new PlaneOrientation(10, 91)));
    }

    [Fact]
    public void An_azimuth_of_a_full_turn_is_north()
    {
        Assert.Equal(0, Azimuth.Normalize(360));
        Assert.Equal(330, Azimuth.Normalize(-30));
        Assert.Equal(5, Azimuth.Normalize(725), 1e-9);
        Assert.Equal(0, Azimuth.Normalize(360 - 1e-12));
        Assert.Throws<ArgumentOutOfRangeException>(() => Azimuth.Normalize(double.NaN));
    }

    [Fact]
    public void A_bearing_and_the_same_bearing_taken_backwards_fold_onto_one_axis()
    {
        Assert.Equal(10d, Azimuth.FoldAxial(10d));
        Assert.Equal(10d, Azimuth.FoldAxial(190d), 1e-12);
        Assert.Equal(170d, Azimuth.FoldAxial(350d), 1e-12);
        Assert.Equal(170d, Azimuth.FoldAxial(170d));

        // The half-circle is closed at zero and open at 180: due north and due south are one axis.
        Assert.Equal(0d, Azimuth.FoldAxial(0d));
        Assert.Equal(0d, Azimuth.FoldAxial(180d));
        Assert.Equal(0d, Azimuth.FoldAxial(360d));

        // Values outside a single turn, in both directions, are still bearings.
        Assert.Equal(170d, Azimuth.FoldAxial(-10d), 1e-12);
        Assert.Equal(10d, Azimuth.FoldAxial(730d), 1e-12);

        // A bearing a hair below zero must not round onto the excluded upper edge.
        Assert.Equal(0d, Azimuth.FoldAxial(-1e-15));
        Assert.Throws<ArgumentOutOfRangeException>(() => Azimuth.FoldAxial(double.PositiveInfinity));
    }
}
