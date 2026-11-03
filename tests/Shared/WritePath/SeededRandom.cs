namespace Therion.Testing;

/// <summary>
/// A deterministic pseudo-random source for generated test cases (SplitMix64). The same seed gives
/// the same sequence on every runtime and OS, which <see cref="Random"/> does not promise, so a
/// failing case can be replayed from the seed printed in its assertion message.
/// </summary>
internal sealed class SeededRandom(ulong seed)
{
    private ulong _state = seed;

    public ulong NextUInt64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A value in [0, <paramref name="maxExclusive"/>).</summary>
    public int Next(int maxExclusive) => maxExclusive <= 1 ? 0 : (int)(NextUInt64() % (ulong)maxExclusive);

    /// <summary>A value in [<paramref name="min"/>, <paramref name="maxExclusive"/>).</summary>
    public int Next(int min, int maxExclusive) => min + Next(maxExclusive - min);

    /// <summary>True with the given probability in percent.</summary>
    public bool Chance(int percent) => Next(100) < percent;

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];
}
