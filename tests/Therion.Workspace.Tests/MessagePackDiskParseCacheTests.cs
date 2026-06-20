// Post-M6 follow-up D — MessagePack disk cache backend.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;
using Therion.Workspace;

namespace Therion.Workspace.Tests;

public class MessagePackDiskParseCacheTests
{
    private static string FreshRoot()
    {
        var p = Path.Combine(Path.GetTempPath(), "thp_mpack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Set_then_TryGet_returns_reparsed_result()
    {
        var root = FreshRoot();
        try
        {
            var file = Path.Combine(root, "cave.th");
            File.WriteAllText(file, "survey x\n  centreline\n  endcentreline\nendsurvey\n");
            var fi = new FileInfo(file);
            var key = new ParseCacheKey(file, fi.Length, fi.LastWriteTimeUtc, TherionSyntaxVersion.Default);

            var disk = new MessagePackDiskParseCache(root);
            disk.Set(key, new ParseResult<TherionFile>(null, ImmutableArray<Diagnostic>.Empty));

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
            var disk = new MessagePackDiskParseCache(root);
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
            var disk = new MessagePackDiskParseCache(root);
            disk.Set(key, new ParseResult<TherionFile>(null, ImmutableArray<Diagnostic>.Empty));
            disk.Invalidate(file);
            Assert.False(disk.TryGet(key, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Files_have_msgpack_extension()
    {
        var root = FreshRoot();
        try
        {
            var file = Path.Combine(root, "a.th");
            File.WriteAllText(file, "survey a\nendsurvey\n");
            var fi = new FileInfo(file);
            var key = new ParseCacheKey(file, fi.Length, fi.LastWriteTimeUtc, TherionSyntaxVersion.Default);
            var disk = new MessagePackDiskParseCache(root);
            disk.Set(key, new ParseResult<TherionFile>(null, ImmutableArray<Diagnostic>.Empty));

            Assert.NotEmpty(Directory.GetFiles(root, "*.msgpack"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
