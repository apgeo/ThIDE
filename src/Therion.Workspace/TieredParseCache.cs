// Implementation Plan §4.5 — two-tier cache: in-memory (L1) + disk (L2).
// On a miss in L1 we consult L2; a hit promotes the entry into L1.

using Therion.Syntax;

namespace Therion.Workspace;

public sealed class TieredParseCache : IParseCache
{
    private readonly IParseCache _memory;
    private readonly IDiskParseCache _disk;

    public TieredParseCache(IParseCache memory, IDiskParseCache disk)
    {
        _memory = memory;
        _disk = disk;
    }

    public bool TryGet(ParseCacheKey key, out ParseResult<TherionFile> result)
    {
        if (_memory.TryGet(key, out result)) return true;
        if (_disk.TryGet(key, out result))
        {
            _memory.Set(key, result);
            return true;
        }
        return false;
    }

    public void Set(ParseCacheKey key, ParseResult<TherionFile> result)
    {
        _memory.Set(key, result);
        _disk.Set(key, result);
    }

    public void Invalidate(string absolutePath)
    {
        _memory.Invalidate(absolutePath);
        _disk.Invalidate(absolutePath);
    }

    public void InvalidateAll()
    {
        _memory.InvalidateAll();
        _disk.InvalidateAll();
    }
}
