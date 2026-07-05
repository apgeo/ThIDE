using System.Linq;
using Therion.Core;
using ThIDE.Services;

namespace ThIDE.Tests;

// (persistent symbol index) + (string interner) infrastructure.
public class PerfInfraTests
{
    // ---- : StringInterner ----

    [Fact]
    public void Interner_returns_same_instance_for_equal_strings()
    {
        var interner = new StringInterner();
        var a = string.Concat("sta", "tion");      // distinct instance from the literal
        var b = string.Concat("stat", "ion");
        Assert.NotSame(a, b);

        var ia = interner.Intern(a);
        var ib = interner.Intern(b);
        Assert.Same(ia, ib);
        Assert.Equal("station", ia);
    }

    [Fact]
    public void Interner_handles_null_and_empty()
    {
        var interner = new StringInterner();
        Assert.Equal(string.Empty, interner.Intern(null));
        Assert.Equal(string.Empty, interner.Intern(string.Empty));
        Assert.Equal(0, interner.Count);
    }

    [Fact]
    public void Interner_stops_growing_past_cap()
    {
        var interner = new StringInterner(maxSize: 2);
        interner.Intern("a");
        interner.Intern("b");
        var c = string.Concat("c", "");
        Assert.Same(c, interner.Intern(c));   // cap hit → original returned, not pooled
        Assert.Equal(2, interner.Count);
    }

    // ---- : WorkspaceSymbolIndexStore ----

    [Fact]
    public void Symbol_index_round_trips_through_disk()
    {
        using var dir = new TempDir();
        var store = new WorkspaceSymbolIndexStore(dir.Path);
        var index = new WorkspaceSymbolIndex
        {
            Root = "C:/proj",
            Symbols = new[]
            {
                new IndexedSymbol("a.b", "station", "C:/proj/a.th", 3, 1, 3, 4, 42, 3),
                new IndexedSymbol("main", "survey", "C:/proj/a.th", 1, 1, 1, 5, 0, 4),
            },
        };

        store.Save(index);
        var loaded = store.Load("C:/proj");

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Symbols.Count);
        var station = loaded.Symbols.First(s => s.Kind == "station");
        Assert.Equal("a.b", station.Name);

        var span = station.ToSpan();
        Assert.Equal("C:/proj/a.th", span.FilePath);
        Assert.Equal(3, span.Start.Line);
        Assert.Equal(42, span.StartOffset);
        Assert.Equal(3, span.Length);
        Assert.False(span.IsEmpty);   // navigable
    }

    [Fact]
    public void Symbol_index_load_for_unknown_root_is_null()
    {
        using var dir = new TempDir();
        var store = new WorkspaceSymbolIndexStore(dir.Path);
        Assert.Null(store.Load("C:/never-saved"));
        Assert.Null(store.Load(null));
    }

    [Fact]
    public void Symbol_index_with_null_root_is_not_persisted()
    {
        using var dir = new TempDir();
        var store = new WorkspaceSymbolIndexStore(dir.Path);
        store.Save(new WorkspaceSymbolIndex { Root = null, Symbols = System.Array.Empty<IndexedSymbol>() });
        // Nothing keyable was written; a later load by any root still finds nothing.
        Assert.Null(store.Load("C:/proj"));
    }
}
