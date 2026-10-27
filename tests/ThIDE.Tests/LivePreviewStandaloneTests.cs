using Therion.Semantics;
using Therion.Syntax;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

/// <summary>The preview hosted in a dialog: given models, a link not yet written, and highlights.</summary>
public class LivePreviewStandaloneTests
{
    private static SemanticModel Model(string path, string survey, int compass) =>
        new SemanticBinder().Bind(new ThParser().Parse(path,
            $"survey {survey}\n  centreline\n    data normal from to length compass clino\n    1 2 10 {compass} 0\n  endcentreline\nendsurvey\n").Value!);

    [Fact]
    public void Draws_only_the_given_models()
    {
        var vm = LivePreviewViewModel.CreateStandalone();

        vm.SetModels([Model("a.th", "a", 90)]);

        Assert.Single(vm.Segments);
        Assert.Equal("a.th", vm.Segments[0].File);
    }

    [Fact]
    public void A_virtual_equate_attaches_the_new_survey_where_the_link_will_put_it()
    {
        var vm = LivePreviewViewModel.CreateStandalone();
        var a = Model("a.th", "a", 90);   // 1 → 2 heading east
        var b = Model("b.th", "b", 0);    // 1 → 2 heading north

        vm.SetModels([a, b], [(QualifiedName.Of("a", "2"), QualifiedName.Of("b", "1"))], emphasizedFile: "b.th");

        var leg = vm.Segments.Single(s => s.File == "b.th");
        var east = vm.Segments.Single(s => s.File == "a.th");
        Assert.Equal(east.X2, leg.X1, 6);   // b starts where a ends
        Assert.Equal(east.Y2, leg.Y1, 6);
        Assert.Equal("b.th", vm.EmphasizedFile);
    }

    [Fact]
    public void Highlighted_stations_follow_rebuilds()
    {
        var vm = LivePreviewViewModel.CreateStandalone();
        vm.SetModels([Model("a.th", "a", 90)]);

        vm.SetHighlightedStations([QualifiedName.Of("a", "2")]);
        Assert.Single(vm.HighlightedStations);

        vm.SetModels([Model("a.th", "a", 90), Model("b.th", "b", 0)], [(QualifiedName.Of("b", "2"), QualifiedName.Of("a", "1"))]);
        var ring = Assert.Single(vm.HighlightedStations);
        var leg = vm.Segments.Single(s => s.File == "a.th");

        Assert.Equal("a.2", ring.Name);
        Assert.Equal((leg.X2, leg.Y2), (ring.X, ring.Y)); // drawn where a.2 now is
    }
}
