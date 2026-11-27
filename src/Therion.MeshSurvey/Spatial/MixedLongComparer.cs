// Packed 64-bit keys (an edge's two vertex indices, a voxel's three coordinates) hash terribly with
// long's own GetHashCode, which XORs the two halves: (a << 32 | b) collides for every a ^ b. This
// comparer mixes all the bits (the SplitMix64 finaliser) so dictionaries of such keys stay O(1).

namespace Therion.MeshSurvey.Spatial;

internal sealed class MixedLongComparer : IEqualityComparer<long>
{
    public static readonly MixedLongComparer Instance = new();

    public bool Equals(long x, long y) => x == y;

    public int GetHashCode(long key)
    {
        ulong z = unchecked((ulong)key + 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (int)(z ^ (z >> 31));
    }
}
