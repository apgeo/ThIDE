// M5 � IParseCache + TherionWorkspace tests.

using System.Collections.Immutable;
using System.IO;
using Therion.Core;
using Therion.Syntax;
using Therion.Workspace;

namespace Therion.Workspace.Tests;

public class ParseCacheTests
{
    [Fact]
    public void Cache_hit_when_key_matches()
    {
        var cache = new InMemoryParseCache();
        var key = new ParseCacheKey("a.th", 10, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TherionSyntaxVersion.Default);
        var pr = new ParseResult<TherionFile>(
            new TherionFile(SourceSpan.None, "a.th",
                ImmutableArray<TherionNode>.Empty, TherionSyntaxVersion.Default),
            ImmutableArray<Diagnostic>.Empty);
        cache.Set(key, pr);

        Assert.True(cache.TryGet(key, out var got));
        Assert.Same(pr, got);
    }

    [Fact]
    public void Cache_miss_when_length_changes()
    {
        var cache = new InMemoryParseCache();
        var when = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var k1 = new ParseCacheKey("a.th", 10, when, TherionSyntaxVersion.Default);
        var pr = new ParseResult<TherionFile>(null, ImmutableArray<Diagnostic>.Empty);
        cache.Set(k1, pr);

        var k2 = k1 with { Length = 11 };
        Assert.False(cache.TryGet(k2, out _));
    }

    [Fact]
    public void Invalidate_removes_entry()
    {
        var cache = new InMemoryParseCache();
        var key = new ParseCacheKey("a.th", 1, default, TherionSyntaxVersion.Default);
        cache.Set(key, new ParseResult<TherionFile>(null, ImmutableArray<Diagnostic>.Empty));
        cache.Invalidate("a.th");
        Assert.False(cache.TryGet(key, out _));
    }
}

public class TherionWorkspaceTests
{
    [Fact]
    public async Task Load_parses_entry_point_and_tracks_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "thconfig");
        await File.WriteAllTextAsync(path, "source cave.th\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(path);

        Assert.Equal(Path.GetFullPath(path), ws.EntryPointPath);
        Assert.Contains(ws.LoadedFiles, p =>
            string.Equals(p, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Load_follows_source_directives()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = Path.Combine(dir, "thconfig");
        var th  = Path.Combine(dir, "cave.th");
        await File.WriteAllTextAsync(cfg, "source cave.th\n");
        await File.WriteAllTextAsync(th, "survey s\nendsurvey\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);

        Assert.Contains(ws.LoadedFiles, p =>
            string.Equals(p, Path.GetFullPath(th), StringComparison.OrdinalIgnoreCase));

        Directory.Delete(dir, recursive: true);
    }

    // An extensionless `source B` reaches whichever candidate is on disk — `.th` preferred,
    // `.th2` as the fallback the editor's ctrl-click has always used — so the BFS loads the
    // same file the editor opens.
    [Fact]
    public async Task Load_follows_an_extensionless_include_to_its_th2_target()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = Path.Combine(dir, "thconfig");
        var th2 = Path.Combine(dir, "cave.th2");
        await File.WriteAllTextAsync(cfg, "source cave\n");
        await File.WriteAllTextAsync(th2, "scrap s1\nendscrap\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);

        Assert.Contains(ws.LoadedFiles, p =>
            string.Equals(p, Path.GetFullPath(th2), StringComparison.OrdinalIgnoreCase));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Load_prefers_the_default_th_over_a_th2_sibling()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = Path.Combine(dir, "thconfig");
        var th  = Path.Combine(dir, "cave.th");
        var th2 = Path.Combine(dir, "cave.th2");
        await File.WriteAllTextAsync(cfg, "source cave\n");
        await File.WriteAllTextAsync(th, "survey s\nendsurvey\n");
        await File.WriteAllTextAsync(th2, "scrap s1\nendscrap\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);

        Assert.Contains(ws.LoadedFiles, p =>
            string.Equals(p, Path.GetFullPath(th), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ws.LoadedFiles, p =>
            string.Equals(p, Path.GetFullPath(th2), StringComparison.OrdinalIgnoreCase));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task InvalidateAll_clears_files_and_raises_event()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "thconfig");
        await File.WriteAllTextAsync(path, "source x.th\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(path);

        int changed = 0;
        ws.WorkspaceChanged += (_, _) => changed++;
        ws.InvalidateAll();

        Assert.Empty(ws.LoadedFiles);
        Assert.Equal(1, changed);

        Directory.Delete(dir, recursive: true);
    }
}
