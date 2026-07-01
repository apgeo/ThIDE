// minimal 3-D vector for the structural-geology core.
//
// Frame: E(ast), N(orth), Z(up) — the same geographically-correct convention the Live Preview uses
// (E = horiz·sin(compass), N = horiz·cos(compass), Z = len·sin(clino)). Distances in metres.

using System;

namespace Therion.Structural;

/// <summary>A 3-D vector in the local cave frame: East, North, Up. Distances in metres.</summary>
public readonly record struct Vec3(double E, double N, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.E + b.E, a.N + b.N, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.E - b.E, a.N - b.N, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.E * s, a.N * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.E / s, a.N / s, a.Z / s);

    public double Dot(Vec3 b) => E * b.E + N * b.N + Z * b.Z;

    public Vec3 Cross(Vec3 b) =>
        new(N * b.Z - Z * b.N, Z * b.E - E * b.Z, E * b.N - N * b.E);

    public double LengthSquared => E * E + N * N + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Unit vector in the same direction; returns <see cref="Zero"/> for a (near-)zero vector.</summary>
    public Vec3 Normalized()
    {
        double len = Length;
        return len < 1e-300 ? Zero : this / len;
    }
}
