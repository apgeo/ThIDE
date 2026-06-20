// Implementation Plan §4.5 — parse cache abstraction.
// Two-level: in-memory (this M5) + disk MessagePack (follow-up).

using Therion.Core;
using Therion.Syntax;

namespace Therion.Workspace;

/// <summary>Cache key for a parsed file (Implementation Plan §4.5).</summary>
public readonly record struct ParseCacheKey(
    string AbsolutePath,
    long Length,
    DateTime LastWriteUtc,
    TherionSyntaxVersion Version);

/// <summary>
/// Stores parsed <see cref="TherionFile"/> results keyed by file fingerprint.
/// In-memory tier; the disk MessagePack tier is added in a follow-up.
/// </summary>
public interface IParseCache
{
    bool TryGet(ParseCacheKey key, out ParseResult<TherionFile> result);
    void Set(ParseCacheKey key, ParseResult<TherionFile> result);
    void Invalidate(string absolutePath);
    void InvalidateAll();
}
