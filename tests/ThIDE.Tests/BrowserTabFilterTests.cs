// Object Browser per-tab filtering: the free-text box (multi-term, matched across all visible
// fields) composed with an optional custom subset filter (the Overview ▸ Quality drill-downs).

using System.Linq;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class BrowserTabFilterTests
{
    private static StationRow S(string qualifiedName, string survey) =>
        new(qualifiedName, "station", survey, "f.th", 1);

    private static string[] Names(BrowserTabFilter f) =>
        f.Items.Cast<StationRow>().Select(r => r.QualifiedName).ToArray();

    [Fact]
    public void No_filter_returns_all_rows()
    {
        var f = new BrowserTabFilter(BrowserTab.Stations);
        f.SetSource(new object[] { S("cave.a", "cave"), S("cave.b", "cave") });
        Assert.Equal(new[] { "cave.a", "cave.b" }, Names(f));
    }

    [Fact]
    public void Text_matches_across_fields_case_insensitively()
    {
        var f = new BrowserTabFilter(BrowserTab.Stations);
        f.SetSource(new object[] { S("cave.entrance", "cave"), S("pit.p1", "pit") });
        f.Text = "ENT";                     // only cave.entrance contains "ent" (in its name)
        Assert.Equal(new[] { "cave.entrance" }, Names(f));
    }

    [Fact]
    public void Multiple_terms_are_ANDed_across_fields()
    {
        var f = new BrowserTabFilter(BrowserTab.Stations);
        f.SetSource(new object[] { S("cave.a", "north"), S("cave.b", "south") });
        f.Text = "cave north";              // "cave" via name, "north" via survey → only cave.a
        Assert.Equal(new[] { "cave.a" }, Names(f));
    }

    [Fact]
    public void Custom_filter_composes_with_text_then_clears()
    {
        var f = new BrowserTabFilter(BrowserTab.Stations);
        f.SetSource(new object[] { S("cave.a", "cave"), S("cave.bb", "cave"), S("pit.a", "pit") });

        f.ApplyCustom("A-stations", o => o is StationRow r && r.QualifiedName.EndsWith(".a"));
        Assert.True(f.HasCustomFilter);
        Assert.Equal("A-stations", f.CustomLabel);
        Assert.Equal(new[] { "cave.a", "pit.a" }, Names(f));

        f.Text = "cave";                    // AND with the custom subset
        Assert.Equal(new[] { "cave.a" }, Names(f));

        f.ClearCustomFilterCommand.Execute(null);
        Assert.False(f.HasCustomFilter);
        Assert.Null(f.CustomLabel);
        Assert.Equal(new[] { "cave.a", "cave.bb" }, Names(f));  // text filter survives the clear
    }
}
