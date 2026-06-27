// LANG-09 — symbol-set / symbol-hide/show model.
// LANG-10 — layout + `code … endcode` recognized in .th files too (not just .thconfig).

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class LayoutSymbolsTests
{
    [Fact]
    public void Symbol_set_and_directives_are_modeled()
    {
        var file = new ThconfigParser().Parse("x.thconfig", """
            layout l1
              symbol-set UIS
              symbol-hide point cave-station
              symbol-show line survey
              symbol-hide group cave-centerline
            endlayout
            """).Value!;
        var layout = file.Children.OfType<LayoutCommand>().Single();
        Assert.Equal("UIS", layout.SymbolSet);
        Assert.Equal(3, layout.SymbolDirectives.Length);
        Assert.Contains(layout.SymbolDirectives,
            d => d.Action == SymbolDirectiveKind.Hide && d.Kind == "point" && d.Symbol == "cave-station");
        Assert.Contains(layout.SymbolDirectives,
            d => d.Action == SymbolDirectiveKind.Show && d.Kind == "line" && d.Symbol == "survey");
    }

    [Fact]
    public void Known_symbol_set_standards_are_recognized()
    {
        Assert.True(SymbolSets.IsKnownStandard("UIS"));
        Assert.True(SymbolSets.IsKnownStandard("skbb"));
        Assert.False(SymbolSets.IsKnownStandard("nonsense"));
    }

    // ---- LANG-10: layout + code blocks inside a .th file ----

    [Fact]
    public void Layout_block_in_th_file_is_consumed_without_unknown_command_warnings()
    {
        var r = new ThParser().Parse("/p/layout.th", """
            layout my_layout
              scale 1 500
              legend on
              code metapost
                def l_contour_MY (expr P)(text txt) =
                  draw P;
                enddef;
              endcode
              symbol-set SKBB
            endlayout
            """);
        // None of layout / scale / legend / code / def / enddef / endcode should be "unknown".
        Assert.DoesNotContain(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnknownCommand);
        var layout = r.Value!.Children.OfType<LayoutCommand>().Single();
        Assert.Equal("my_layout", layout.Id);
        Assert.True(layout.IsTerminated);
        Assert.Equal("SKBB", layout.SymbolSet);
        Assert.Single(layout.CodeBlocks);
        Assert.Equal("metapost", layout.CodeBlocks[0].Language);
        // The metapost def line must not leak in as a layout option.
        Assert.DoesNotContain(layout.Options, o => o.Key == "def");
    }

    [Fact]
    public void Layout_in_th_does_not_swallow_following_survey()
    {
        var r = new ThParser().Parse("/p/a.th", """
            layout l
              scale 1 100
            endlayout
            survey s
            endsurvey
            """);
        Assert.Single(r.Value!.Children.OfType<LayoutCommand>());
        Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
    }
}
