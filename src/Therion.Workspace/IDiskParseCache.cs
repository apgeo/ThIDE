// Implementation Plan §4.5 — on-disk tier (Decision #8).
// Production default is on; can be skipped via `WorkspaceOptions.DisableDiskCache`,
// CLI `--no-cache`, or env var `THERIONPROC_NO_CACHE`.

using Therion.Syntax;

namespace Therion.Workspace;

/// <summary>
/// Persistent parse cache. Lives next to <see cref="IParseCache"/> as the L2 tier
/// behind <see cref="InMemoryParseCache"/>. Default impl is JSON-backed
/// (<see cref="JsonDiskParseCache"/>); a MessagePack impl can be slotted in later
/// without touching the workspace.
/// </summary>
public interface IDiskParseCache
{
    bool TryGet(ParseCacheKey key, out ParseResult<TherionFile> result);
    void Set(ParseCacheKey key, ParseResult<TherionFile> result);
    void Invalidate(string absolutePath);
    void InvalidateAll();
}

/// <summary>No-op disk cache — used when caching is disabled.</summary>
public sealed class NullDiskParseCache : IDiskParseCache
{
    public static NullDiskParseCache Instance { get; } = new();
    public bool TryGet(ParseCacheKey key, out ParseResult<TherionFile> result) { result = default!; return false; }
    public void Set(ParseCacheKey key, ParseResult<TherionFile> result) { }
    public void Invalidate(string absolutePath) { }
    public void InvalidateAll() { }
}
