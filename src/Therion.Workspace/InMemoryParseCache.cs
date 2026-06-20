// Implementation Plan §4.5 — in-memory tier.

using System.Collections.Concurrent;
using Therion.Syntax;

namespace Therion.Workspace;

public sealed class InMemoryParseCache : IParseCache
{
    private readonly ConcurrentDictionary<string, (ParseCacheKey Key, ParseResult<TherionFile> Result)> _byPath
        = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(ParseCacheKey key, out ParseResult<TherionFile> result)
    {
        if (_byPath.TryGetValue(key.AbsolutePath, out var entry) && entry.Key == key)
        {
            result = entry.Result;
            return true;
        }
        result = default!;
        return false;
    }

    public void Set(ParseCacheKey key, ParseResult<TherionFile> result)
        => _byPath[key.AbsolutePath] = (key, result);

    public void Invalidate(string absolutePath) => _byPath.TryRemove(absolutePath, out _);

    public void InvalidateAll() => _byPath.Clear();
}
