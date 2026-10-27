using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Linking;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Pickers;

namespace ThIDE.Tests;

public class PickerViewModelTests
{
    private static PickerItem Item(string primary, string search) => new(primary, null, search, 0, primary);

    [Fact]
    public void Filter_keeps_rows_holding_every_term_in_any_case()
    {
        var items = new[] { Item("1", "cave.a.1 a.th"), Item("2", "cave.b.2 b.th"), Item("3", "Cave.A.3 a.th") };

        Assert.Equal(["1", "3"], ItemPickerViewModel.Matches(items, "a.th CAVE").Select(i => i.Primary));
        Assert.Equal(3, ItemPickerViewModel.Matches(items, "  ").Count);
    }

    [Fact]
    public void Selecting_a_filtered_out_row_clears_the_filter()
    {
        var picker = new ItemPickerViewModel("T");
        picker.SetItems([Item("1", "one"), Item("2", "two")]);
        picker.Filter = "one";

        Assert.True(picker.SelectByPayload(p => (string)p == "2"));

        Assert.Equal("", picker.Filter);
        Assert.Equal(2, picker.Visible.Count);
        Assert.Equal("2", picker.Selected!.Primary);
    }

    [Fact]
    public void Selection_survives_a_reload_when_its_payload_is_still_there()
    {
        var picker = new ItemPickerViewModel("T");
        picker.SetItems([Item("1", "one"), Item("2", "two")]);
        picker.SelectByPayload(p => (string)p == "2");

        picker.SetItems([Item("2", "two"), Item("3", "three")]);

        Assert.Equal("2", picker.Selected!.Primary);
    }

    [Fact]
    public void Clicking_a_station_in_the_preview_selects_it_and_rings_it()
    {
        var ast = new ThParser().Parse("new.th",
            "survey t\n  centreline\n    data normal from to length compass clino\n    1 2 10 0 0\n    2 10 5 90 0\n  endcentreline\nendsurvey\n").Value!;
        var source = new SourceSurvey("new.th", ast, new SemanticBinder().Bind(ast));
        var selector = new StationSelectorViewModel("Source");
        selector.Load(LinkCatalog.Stations(source), [source.Model], showFile: false);

        Assert.Equal(["1", "2", "10"], selector.Picker.Items.Select(i => i.Primary));

        var point = selector.Preview.StationPoints.Single(p => p.Name == "t.10");
        selector.Preview.ActivateStation(point);

        Assert.Equal("t.10", selector.Picker.SelectedStation!.Local.ToString());
        Assert.Equal("t.10", Assert.Single(selector.Preview.HighlightedStations).Name);
    }
}
