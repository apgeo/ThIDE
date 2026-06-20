// M5 follow-up — JSON disk cache tier.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;
using Therion.Workspace;

namespace Therion.Workspace.Tests;

public class JsonDiskParseCacheTests
{
    private static string FreshRoot()
    {
        var p = Path.Combine(Path.GetTempPath(), "thp_disk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Set_then_TryGet_returns_reparsed_result()
    {
        var root = FreshRoot();
        try
        {
            var dir = Path.Combine(root, "src");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "cave.th");
            File.WriteAllText(file, "survey x\n  centreline\n  endcentreline\nendsurvey\n");
            var fi = new FileInfo(file);
            var key = new ParseCacheKey(file, fi.Length, fi.LastWriteTimeUtc, TherionSyntaxVersion.Default);

            var disk = new JsonDiskParseCache(root);
            var initial = new ParseResult<TherionFile>(
                new TherionFile(SourceSpan.None, file, ImmutableArray<TherionNode>.Empty, TherionSyntaxVersion.Default),
                ImmutableArray<Diagnostic>.Empty);
            disk.Set(key, initial);

            Assert.True(disk.TryGet(key, out var got));
            Assert.NotNull(got.Value);
            Assert.Equal(file, got.Value!.Path);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void TryGet_miss_when_length_changes()
    {
        var root = FreshRoot();
        try
        {
            var file = Path.Combine(root, "a.th");
            File.WriteAllText(file, "survey a\nendsurvey\n");
            var fi = new FileInfo(file);
            var k1 = new ParseCacheKey(file, fi.Length, fi.LastWriteTimeUtc, TherionSyntaxVersion.Default);
            var disk = new JsonDiskParseCache(root);
            disk.Set(k1, new ParseResult<TherionFile>(null, ImmutableArray<Diagnostic>.Empty));

            var k2 = k1 with { Length = k1.Length + 1 };
            Assert.False(disk.TryGet(k2, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Invalidate_removes_entry()
    {
        var root = FreshRoot();
        try
        {
            var file = Path.Combine(root, "a.th");
            File.WriteAllText(file, "survey a\nendsurvey\n");
            var fi = new FileInfo(file);
            var key = new ParseCacheKey(file, fi.Length, fi.LastWriteTimeUtc, TherionSyntaxVersion.Default);
            var disk = new JsonDiskParseCache(root);
            disk.Set(key, new ParseResult<TherionFile>(null, ImmutableArray<Diagnostic>.Empty));
            disk.Invalidate(file);
            Assert.False(disk.TryGet(key, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}

public class TieredParseCacheTests
{
    [Fact]
    public void Disk_hit_promotes_to_memory()
    {
        var root = Path.Combine(Path.GetTempPath(), "thp_tier_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "a.th");
            File.WriteAllText(file, "survey a\nendsurvey\n");
            var fi = new FileInfo(file);
            var key = new ParseCacheKey(file, fi.Length, fi.LastWriteTimeUtc, TherionSyntaxVersion.Default);

            var mem = new InMemoryParseCache();
            var disk = new JsonDiskParseCache(root);
            disk.Set(key, new ParseResult<TherionFile>(
                new TherionFile(SourceSpan.None, file, ImmutableArray<TherionNode>.Empty, TherionSyntaxVersion.Default),
                ImmutableArray<Diagnostic>.Empty));

            var tiered = new TieredParseCache(mem, disk);
            Assert.True(tiered.TryGet(key, out _));
            // Second call should now hit memory tier (still succeeds).
            Assert.True(mem.TryGet(key, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Null_disk_cache_is_bypass()
    {
        var mem = new InMemoryParseCache();
        var tiered = new TieredParseCache(mem, NullDiskParseCache.Instance);
        var key = new ParseCacheKey("x.th", 1, default, TherionSyntaxVersion.Default);
        Assert.False(tiered.TryGet(key, out _));
    }
}
