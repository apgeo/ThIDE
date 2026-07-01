// STRUCT-01 Phase 2 test helpers — build SemanticModels directly from synthetic shots/stations
// (no parser dependency), plus polar/planar generators shared across the detection/analysis tests.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Semantics;

namespace Therion.Structural.Tests;

internal static class TestModel
{
    public static QualifiedName QN(string s) => QualifiedName.Parse(s);

    public static SourceSpan Span(int line = 1, string file = "cave.th") =>
        new(file, new SourceLocation(line, 1), new SourceLocation(line, 2), 0, 1);

    /// <summary>A shot whose geometry is given by an explicit local vector (converted back to polar).</summary>
    public static ShotSymbol Shot(string from, string to, Vec3 vec,
        ShotFlags flags = ShotFlags.None, string? comment = null, int line = 1)
    {
        var (len, compass, clino) = ToPolar(vec);
        return new ShotSymbol(QN(from), QN(to), len, compass, clino, Span(line)) { Flags = flags, Comment = comment };
    }

    /// <summary>A shot with explicit length / compass° / clino°.</summary>
    public static ShotSymbol RawShot(string from, string to, double len, double compass, double clino,
        ShotFlags flags = ShotFlags.None, string? comment = null, int line = 1)
        => new ShotSymbol(QN(from), QN(to), len, compass, clino, Span(line)) { Flags = flags, Comment = comment };

    public static (double Len, double Compass, double Clino) ToPolar(Vec3 v)
    {
        double len = v.Length;
        double clino = len < 1e-12 ? 0 : Math.Asin(v.Z / len) * 180.0 / Math.PI;
        double compass = PlaneFitter.NormalizeAzimuth(Math.Atan2(v.E, v.N) * 180.0 / Math.PI);
        return (len, compass, clino);
    }

    public static StationSymbol Station(string name, params string[] flags) =>
        new(QN(name), Span(), StationDeclarationKind.Shot, ImmutableArray<SourceSpan>.Empty)
        { Flags = flags.ToImmutableArray() };

    public static SemanticModel Model(IEnumerable<ShotSymbol> shots, IEnumerable<StationSymbol>? stations = null)
    {
        var st = (stations ?? Array.Empty<StationSymbol>()).ToFrozenDictionary(s => s.Name);
        return new SemanticModel(
            st,
            FrozenDictionary<QualifiedName, SurveySymbol>.Empty,
            shots.ToImmutableArray(),
            new EquateGraph(),
            ImmutableArray<Diagnostic>.Empty);
    }

    // ---- planar generators (mirrors PlaneFitterTests) ------------------------------------------

    public static Vec3 NormalFrom(double dipDeg, double dipDirDeg)
    {
        double dip = dipDeg * Math.PI / 180.0, az = dipDirDeg * Math.PI / 180.0;
        return new Vec3(Math.Sin(dip) * Math.Sin(az), Math.Sin(dip) * Math.Cos(az), Math.Cos(dip));
    }

    public static List<Vec3> PlanePoints(double dipDeg, double dipDirDeg, Vec3 center, int perAxis = 4, double spacing = 1.0)
    {
        var normal = NormalFrom(dipDeg, dipDirDeg);
        var reference = Math.Abs(normal.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        var u = normal.Cross(reference).Normalized();
        var w = normal.Cross(u).Normalized();
        var pts = new List<Vec3>();
        double mid = (perAxis - 1) / 2.0;
        for (int i = 0; i < perAxis; i++)
            for (int j = 0; j < perAxis; j++)
                pts.Add(center + u * ((i - mid) * spacing) + w * ((j - mid) * spacing));
        return pts;
    }
}
