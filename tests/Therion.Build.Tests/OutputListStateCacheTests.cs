// The build state kept next to a cached output list: when it was built, and whether a later build
// of the same entry failed (so the listed files are stale). Lists saved before the state existed,
// and caches that keep no state, read as "unknown, not stale".

using System.Collections.Immutable;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace Therion.Build.Tests;

public class OutputListStateCacheTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thp_liststate_").FullName;
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static readonly ImmutableArray<OutputArtifact> One =
        [new OutputArtifact("/proj/cave.lox", "lox", 10, DateTimeOffset.UnixEpoch)];

    [Fact]
    public void State_roundtrips_with_the_list()
    {
        var cache = new JsonOutputArtifactCache(_root);
        var built = new DateTimeOffset(2026, 11, 4, 20, 21, 0, TimeSpan.Zero);

        cache.Save("/proj/main.thconfig", "unknown", One, new OutputListState(built, Stale: true));

        var fresh = new JsonOutputArtifactCache(_root);   // a restart reads it back from disk
        Assert.Equal(new OutputListState(built, true), fresh.LoadState("/proj/main.thconfig", "unknown"));
        Assert.Single(fresh.Load("/proj/main.thconfig", "unknown"));
    }

    [Fact]
    public void A_list_saved_without_state_reads_as_unknown()
    {
        var cache = new JsonOutputArtifactCache(_root);
        cache.Save("/proj/main.thconfig", "unknown", One);

        Assert.Equal(OutputListState.Unknown, cache.LoadState("/proj/main.thconfig", "unknown"));
        Assert.Equal(OutputListState.Unknown, cache.LoadState("/proj/never-built.thconfig", "unknown"));
    }

    [Fact]
    public void A_list_written_by_an_older_version_reads_as_unknown()
    {
        var cache = new JsonOutputArtifactCache(_root);
        cache.Save("/proj/main.thconfig", "unknown", One);
        var file = Assert.Single(Directory.GetFiles(_root, "*.json"));
        File.WriteAllText(file, """{"entry":"/proj/main.thconfig","therion":"unknown","artifacts":[{"path":"/proj/cave.lox","kind":"lox","size":10,"mtime":0}]}""");

        Assert.Equal(OutputListState.Unknown, cache.LoadState("/proj/main.thconfig", "unknown"));
        Assert.Single(cache.Load("/proj/main.thconfig", "unknown"));
    }

    private sealed class ListOnlyCache : IOutputArtifactCache
    {
        public ImmutableArray<OutputArtifact> Saved = ImmutableArray<OutputArtifact>.Empty;
        public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion) => Saved;
        public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts) => Saved = artifacts;
        public void Clear(string entryPointPath, string therionVersion) => Saved = ImmutableArray<OutputArtifact>.Empty;
    }

    [Fact]
    public void A_cache_that_keeps_no_state_still_saves_the_list()
    {
        IOutputArtifactCache cache = new ListOnlyCache();

        cache.Save("/proj/main.thconfig", "unknown", One, new OutputListState(DateTimeOffset.UnixEpoch, true));

        Assert.Single(cache.Load("/proj/main.thconfig", "unknown"));
        Assert.Equal(OutputListState.Unknown, cache.LoadState("/proj/main.thconfig", "unknown"));
    }
}
