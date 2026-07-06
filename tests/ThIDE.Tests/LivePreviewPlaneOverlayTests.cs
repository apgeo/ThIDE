// structural-plane overlay on the live preview — the pure trace-direction projection:
// PlaneTraceDirection maps a plane's (magnetic) strike/dip into the current view's unit 2-D line
// direction — the strike line in plan, the apparent-dip trace in a profile — with a strike-azimuth
// fallback for horizontal planes and no special-casing for vertical dips.

using System;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class LivePreviewPlaneOverlayTests
{
    private const int Precision = 6;

    private static void AssertDir((double X, double Y) actual, double x, double y)
    {
        Assert.Equal(x, actual.X, Precision);
        Assert.Equal(y, actual.Y, Precision);
    }

    [Fact]
    public void Result_is_always_a_unit_vector()
    {
        foreach (var strike in new[] { 0.0, 33.5, 90, 180, 271.2 })
            foreach (var dip in new[] { 0.0, 15, 45, 89, 90 })
                foreach (var (elev, az) in new[] { (false, 90.0), (true, 0.0), (true, 90.0), (true, 217.0) })
                {
                    var d = LivePreviewViewModel.PlaneTraceDirection(strike, dip, elev, az);
                    Assert.Equal(1.0, Math.Sqrt(d.X * d.X + d.Y * d.Y), Precision);
                }
    }

    [Fact]
    public void Plan_view_draws_the_strike_line()
    {
        // Strike 0 (N–S plane, dipping east): a vertical line in plan (screen Y is flipped north).
        AssertDir(LivePreviewViewModel.PlaneTraceDirection(0, 45, isElevation: false), 0, 1);
        // Strike 90 (E–W plane, dipping south): a horizontal line in plan.
        AssertDir(LivePreviewViewModel.PlaneTraceDirection(90, 30, isElevation: false), -1, 0);
    }

    [Fact]
    public void Plan_view_horizontal_plane_falls_back_to_the_strike_azimuth()
    {
        // Dip 0 has no trace in plan (the normal is vertical) — the reported strike is still drawn.
        AssertDir(LivePreviewViewModel.PlaneTraceDirection(0, 0, isElevation: false), 0, -1);
        AssertDir(LivePreviewViewModel.PlaneTraceDirection(90, 0, isElevation: false), 1, 0);
    }

    [Fact]
    public void East_profile_shows_the_apparent_dip()
    {
        // Plane striking N–S, dipping 45° due east, viewed in the east profile: the trace descends
        // at 45° along the section (screen Y grows downward ↔ world Z decreasing).
        var d = LivePreviewViewModel.PlaneTraceDirection(0, 45, isElevation: true, azimuthDeg: 90);
        AssertDir(d, Math.Sqrt(0.5), Math.Sqrt(0.5));
    }

    [Fact]
    public void Vertical_plane_draws_vertical_in_a_dip_direction_profile()
    {
        // Dip 90 (vertical plane striking N–S), east profile: the trace is a vertical line — the
        // normal-based construction needs no tan(90°) special case.
        var d = LivePreviewViewModel.PlaneTraceDirection(0, 90, isElevation: true, azimuthDeg: 90);
        Assert.Equal(0, Math.Abs(d.X), Precision);
        Assert.Equal(1, Math.Abs(d.Y), Precision);
    }

    [Fact]
    public void Profile_along_the_strike_shows_a_horizontal_trace()
    {
        // Plane striking N–S dipping east, viewed in the NORTH profile (section along the strike):
        // the apparent dip is 0 — a horizontal line.
        var d = LivePreviewViewModel.PlaneTraceDirection(0, 45, isElevation: true, azimuthDeg: 0);
        Assert.Equal(1, Math.Abs(d.X), Precision);
        Assert.Equal(0, Math.Abs(d.Y), Precision);
    }

    [Fact]
    public void Oblique_profile_apparent_dip_follows_the_cosine_rule()
    {
        // tan(apparent) = tan(dip) · cos(section azimuth − dip direction). Strike 0 → dip dir 90;
        // section at 45°: tan(apparent) = tan(60°)·cos(45°).
        var d = LivePreviewViewModel.PlaneTraceDirection(0, 60, isElevation: true, azimuthDeg: 45);
        double expected = Math.Atan(Math.Tan(60 * Math.PI / 180) * Math.Cos(45 * Math.PI / 180));
        Assert.Equal(expected, Math.Atan2(Math.Abs(d.Y), Math.Abs(d.X)), Precision);
    }
}
