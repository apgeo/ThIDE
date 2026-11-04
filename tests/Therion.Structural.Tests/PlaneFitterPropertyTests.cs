// Plane fitting over generated planes: any dip 0-90, any dip direction, with noise. The fit must find the
// plane to within what the noise allows, give the same answer for any point order and any translation,
// keep strike = dip direction - 90, add a declination in the sense a rotated survey would, and put every
// pole inside the stereonet disc. Seeds are fixed (Therion.TestSupport.GeneratedRuns).

using Therion.Structural;
using Therion.TestSupport;

namespace Therion.Structural.Tests;

public class PlaneFitterPropertyTests
{
    private const double Deg = Math.PI / 180.0;
    private const int SeedBase = 31_000;

    private sealed record Sample(double Dip, double DipDirection, double Sigma, List<Vec3> Points, Vec3 Normal)
    {
        public override string ToString() => $"dip {Dip:0.###}, dip direction {DipDirection:0.###}, sigma {Sigma:0.###}, {Points.Count} points";
    }

    // Upward unit normal of a plane that dips Dip degrees towards the azimuth DipDirection.
    private static Vec3 NormalFrom(double dipDeg, double dipDirDeg) =>
        new(Math.Sin(dipDeg * Deg) * Math.Sin(dipDirDeg * Deg),
            Math.Sin(dipDeg * Deg) * Math.Cos(dipDirDeg * Deg),
            Math.Cos(dipDeg * Deg));

    private static Sample Generate(int seed)
    {
        var random = new Random(seed);
        // Dip: the exact ends (flat, vertical) are drawn on purpose, not just left to chance.
        double dip = (seed % 8) switch { 0 => 0.0, 1 => 90.0, _ => random.NextDouble() * 90.0 };
        double dipDir = random.NextDouble() * 360.0;
        double sigma = random.Next(4) == 0 ? 0.0 : random.NextDouble() * 0.6;
        int count = random.Next(12, 80);
        double extent = 20 + random.NextDouble() * 60;

        var normal = NormalFrom(dip, dipDir);
        var reference = Math.Abs(normal.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        var u = normal.Cross(reference).Normalized();
        var w = normal.Cross(u).Normalized();

        // A cave-sized offset from the origin, so a fit that loses precision far from it shows.
        var centre = new Vec3((random.NextDouble() - 0.5) * 2e5, (random.NextDouble() - 0.5) * 2e5, (random.NextDouble() - 0.5) * 2e3);
        var points = new List<Vec3>(count);
        for (int i = 0; i < count; i++)
        {
            double a = (random.NextDouble() - 0.5) * 2 * extent, b = (random.NextDouble() - 0.5) * 2 * extent;
            points.Add(centre + u * a + w * b + normal * (sigma * GeneratedRuns.Gaussian(random)));
        }
        return new Sample(dip, dipDir, sigma, points, normal);
    }

    // The angle (radians) the fitted normal may differ from the true one: the noise over the spread of
    // the points, with a wide margin (the in-plane spread of a square of half-width E is E/sqrt(3)).
    private static double NormalTolerance(Sample s)
    {
        double spread = s.Points.Count < 3 ? 1 : 20.0 / Math.Sqrt(3);
        return 1e-6 + 12.0 * s.Sigma / (spread * Math.Sqrt(s.Points.Count));
    }

    private static double AngleBetweenAxes(Vec3 a, Vec3 b) =>
        Math.Acos(Math.Clamp(Math.Abs(a.Normalized().Dot(b.Normalized())), 0, 1));

    private static double AzimuthDifference(double a, double b)
    {
        double d = Math.Abs(PlaneFitter.NormalizeAzimuth(a - b));
        return Math.Min(d, 360 - d);
    }

    // A vertical plane dips to neither side: which of the two opposite azimuths comes out is decided by the
    // rounding of a normal whose Z is zero. Near vertical, the azimuth is compared as an axis (mod 180).
    private static bool NearVertical(double dipDeg) => dipDeg > 89.99;

    private static double AzimuthDifference(double a, double b, bool asAxis)
    {
        double d = AzimuthDifference(a, b);
        return asAxis ? Math.Min(d, 180 - d) : d;
    }

    [Fact]
    public void The_fit_is_within_the_noise_of_the_generated_plane()
    {
        GeneratedRuns.ForEachSeed(SeedBase, 400, seed =>
        {
            var s = Generate(seed);
            var fit = PlaneFitter.Fit(s.Points);
            Assert.True(fit.IsValid, $"{s}: {fit.ErrorReason}");

            double tol = NormalTolerance(s);
            double off = AngleBetweenAxes(fit.Normal, s.Normal);
            Assert.True(off <= tol, $"{s}: normal off by {off / Deg:0.####} deg, allowed {tol / Deg:0.####}");
            Assert.True(Math.Abs(fit.Dip - s.Dip) <= tol / Deg + 1e-6, $"{s}: dip {fit.Dip:0.####}");
            Assert.True(fit.Normal.Z >= 0, $"{s}: the normal must point up");

            // The dip direction is only defined when the plane is tilted by more than the fit's own error.
            if (s.Dip > 3 * (tol / Deg) && s.Dip > 1)
            {
                double allowed = tol / Deg / Math.Sin(s.Dip * Deg) + 1e-6;
                bool axis = s.Dip + 3 * tol / Deg >= 90;
                Assert.True(AzimuthDifference(fit.DipDirection, s.DipDirection, axis) <= allowed,
                    $"{s}: dip direction {fit.DipDirection:0.####}, allowed +-{allowed:0.####}");
            }

            // The scatter around the plane is the noise: at least a fraction of sigma, never more than a few times it.
            Assert.True(fit.RmsResidual <= 2.5 * s.Sigma + 1e-6, $"{s}: rms {fit.RmsResidual:0.####}");
            if (s.Sigma > 0.1 && s.Points.Count >= 30)
                Assert.True(fit.RmsResidual >= 0.4 * s.Sigma, $"{s}: rms {fit.RmsResidual:0.####}");
            Assert.InRange(fit.Planarity, 0, 1.0 / 3.0 + 1e-9);
        });
    }

    [Fact]
    public void Strike_is_the_dip_direction_minus_ninety_and_azimuths_are_in_range()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 10_000, 400, seed =>
        {
            var s = Generate(seed);
            var fit = PlaneFitter.Fit(s.Points);
            Assert.True(fit.IsValid, $"{s}");

            Assert.InRange(fit.Dip, 0, 90);
            Assert.InRange(fit.DipDirection, 0, 360 - 1e-12);
            Assert.InRange(fit.Strike, 0, 360 - 1e-12);
            Assert.Equal(PlaneFitter.NormalizeAzimuth(fit.DipDirection - 90), fit.Strike, 9);
        });
    }

    [Fact]
    public void The_order_of_the_points_does_not_matter()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 20_000, 200, seed =>
        {
            var s = Generate(seed);
            var random = new Random(seed ^ 0x5bd1e995);
            var shuffled = s.Points.OrderBy(_ => random.Next()).ToList();

            var a = PlaneFitter.Fit(s.Points);
            var b = PlaneFitter.Fit(shuffled);

            // Summation order changes the last bits only; far from the origin that is about 1e-9 of the coordinates.
            Assert.True(AngleBetweenAxes(a.Normal, b.Normal) <= 1e-7, $"{s}: normals differ");
            Assert.Equal(a.Dip, b.Dip, 5);
            Assert.Equal(a.RmsResidual, b.RmsResidual, 5);
            Assert.True(AzimuthDifference(a.DipDirection, b.DipDirection, NearVertical(a.Dip)) <= 1e-5 || a.Dip < 1e-3, $"{s}: dip direction differs");
        });
    }

    [Fact]
    public void Moving_every_point_by_the_same_vector_does_not_change_the_plane()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 30_000, 200, seed =>
        {
            var s = Generate(seed);
            var shift = new Vec3(4.5e5 * (seed % 3 - 1), -2.5e5 * (seed % 5 - 2), 1234.5);
            var moved = s.Points.Select(p => p + shift).ToList();

            var a = PlaneFitter.Fit(s.Points);
            var b = PlaneFitter.Fit(moved);

            Assert.True(b.IsValid, $"{s}");
            Assert.True(AngleBetweenAxes(a.Normal, b.Normal) <= 1e-7, $"{s}: normals differ");
            Assert.Equal(a.Dip, b.Dip, 5);
            Assert.Equal(a.RmsResidual, b.RmsResidual, 4);
            Assert.True(AzimuthDifference(a.DipDirection, b.DipDirection, NearVertical(a.Dip)) <= 1e-5 || a.Dip < 1e-3, $"{s}: dip direction differs");
            // The centroid moves with the points.
            Assert.True((b.Centroid - (a.Centroid + shift)).Dot(b.Centroid - (a.Centroid + shift)) < 1e-6, $"{s}: centroid");
        });
    }

    [Fact]
    public void A_declination_turns_the_azimuths_the_way_a_clockwise_rotation_of_the_survey_does()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 40_000, 300, seed =>
        {
            var s = Generate(seed);
            if (s.Dip < 2) return; // a flat plane has no azimuth to turn
            double delta = (new Random(seed).NextDouble() - 0.5) * 80; // degrees, east positive

            // Rotating the survey clockwise by delta (north towards east) is what adding delta to every
            // magnetic bearing does.
            double c = Math.Cos(delta * Deg), sn = Math.Sin(delta * Deg);
            var rotated = s.Points.Select(p => new Vec3(p.E * c + p.N * sn, -p.E * sn + p.N * c, p.Z)).ToList();

            var corrected = PlaneFitter.Fit(s.Points).WithDeclination(delta);
            var refit = PlaneFitter.Fit(rotated);

            bool axis = NearVertical(refit.Dip);
            Assert.True(AzimuthDifference(corrected.DipDirection, refit.DipDirection, axis) <= 1e-4, $"{s}, delta {delta:0.###}: {corrected.DipDirection} vs {refit.DipDirection}");
            Assert.True(AzimuthDifference(corrected.Strike, refit.Strike, axis) <= 1e-4, $"{s}, delta {delta:0.###}: strike");
            Assert.Equal(refit.Dip, corrected.Dip, 6);
            Assert.Equal(delta, corrected.DeclinationApplied, 12);
            Assert.Equal(PlaneFitter.NormalizeAzimuth(corrected.DipDirection - 90), corrected.Strike, 9);
        });
    }

    [Theory]
    [InlineData(StereonetProjection.EqualAngle)]
    [InlineData(StereonetProjection.EqualArea)]
    public void The_pole_of_a_fitted_plane_lies_inside_the_unit_disc(StereonetProjection projection)
    {
        GeneratedRuns.ForEachSeed(SeedBase + 50_000, 300, seed =>
        {
            var s = Generate(seed);
            var fit = PlaneFitter.Fit(s.Points);
            var (trend, plunge) = Stereonet.PoleOfPlane(fit.Strike, fit.Dip);
            var p = Stereonet.ProjectLine(trend, plunge, projection);

            Assert.True(p.X * p.X + p.Y * p.Y <= 1.0 + 1e-9, $"{s}: pole at ({p.X:0.####}, {p.Y:0.####})");

            // The same pole from the fitted normal: the downward normal as a line.
            var (t2, pl2) = Stereonet.ToTrendPlunge(fit.Normal * -1.0);
            Assert.Equal(plunge, pl2, 6);
            if (plunge < 89.999) Assert.True(AzimuthDifference(trend, t2, NearVertical(fit.Dip)) <= 1e-5, $"{s}: pole trend {trend:0.####} vs {t2:0.####}");
        });
    }
}
