// M6 — ModelEditService tests.

using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ModelEditServiceTests
{
    [Fact]
    public void Replaces_a_fix_node_and_emits_new_text()
    {
        const string src = "fix 1 0 0 0\n";
        var parse = new ThParser().Parse("a.th", src);
        var original = parse.Value!.Children.OfType<StationFix>().Single();

        var replacement = original with { X = 10, Y = 20, Z = 30 };
        var svc = new ModelEditService();
        var result = svc.ReplaceNode(parse.Value!, original, replacement);

        Assert.True(result.Success);
        Assert.NotNull(result.UpdatedText);
        Assert.Contains("10", result.UpdatedText!);
        Assert.Contains("20", result.UpdatedText!);
        Assert.Contains("30", result.UpdatedText!);
    }

    [Fact]
    public void Rejects_type_mismatch()
    {
        var parse = new ThParser().Parse("a.th", "fix 1 0 0 0\n");
        var fix = parse.Value!.Children.OfType<StationFix>().Single();
        var other = new EquateCommand(fix.Span, System.Collections.Immutable.ImmutableArray<string>.Empty);
        var result = new ModelEditService().ReplaceNode(parse.Value!, fix, other);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code.Value == "TH_EDIT_001");
    }

    [Fact]
    public void Replaces_deeply_nested_node_inside_survey()
    {
        const string src = """
            survey s
              fix 1 0 0 0
            endsurvey
            """;
        var parse = new ThParser().Parse("a.th", src);
        var survey = parse.Value!.Children.OfType<SurveyCommand>().Single();
        var fix = survey.Children.OfType<StationFix>().Single();

        var replacement = fix with { Station = "renamed" };
        var result = new ModelEditService().ReplaceNode(parse.Value!, fix, replacement);

        Assert.True(result.Success);
        Assert.Contains("renamed", result.UpdatedText!);
    }
}
