// §9bis.2 — output artifact cache tests.

using System.Collections.Immutable;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace Therion.Build.Tests;

public class OutputArtifactCacheTests
{
    private static string FreshRoot()
    {
        var p = Path.Combine(Path.GetTempPath(), "thp_artcache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var root = FreshRoot();
        try
        {
            var cache = new JsonOutputArtifactCache(root);
            var artifacts = ImmutableArray.Create(
                new OutputArtifact("/proj/out/cave.lox", "Loch 3D model", 1234,
                    new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero)),
                new OutputArtifact("/proj/out/cave.3d", "Survex 3D model", 567,
                    new DateTimeOffset(2024, 6, 1, 12, 0, 1, TimeSpan.Zero)));
            cache.Save("/proj/thconfig", "6.4.0", artifacts);

            var loaded = cache.Load("/proj/thconfig", "6.4.0");
            Assert.Equal(2, loaded.Length);
            Assert.Equal("/proj/out/cave.lox", loaded[0].Path);
            Assert.Equal(1234, loaded[0].SizeBytes);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Load_returns_empty_when_version_changes()
    {
        var root = FreshRoot();
        try
        {
            var cache = new JsonOutputArtifactCache(root);
            cache.Save("/proj/thconfig", "6.4.0",
                ImmutableArray.Create(new OutputArtifact("a", "k", 1, DateTimeOffset.UnixEpoch)));
            Assert.Empty(cache.Load("/proj/thconfig", "6.5.0"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Clear_removes_entry()
    {
        var root = FreshRoot();
        try
        {
            var cache = new JsonOutputArtifactCache(root);
            cache.Save("/proj/thconfig", "6.4.0",
                ImmutableArray.Create(new OutputArtifact("a", "k", 1, DateTimeOffset.UnixEpoch)));
            cache.Clear("/proj/thconfig", "6.4.0");
            Assert.Empty(cache.Load("/proj/thconfig", "6.4.0"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
