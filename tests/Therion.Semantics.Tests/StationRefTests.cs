// Unit tests for StationRef — Therion's `point@inner.outer` reference notation
// and the click-part disambiguation used by the editor.

using Therion.Semantics;

namespace Therion.Semantics.Tests;

public class StationRefTests
{
    [Fact]
    public void Parse_splits_point_and_reverses_survey_path()
    {
        var r = StationRef.Parse("11@grind_175_baza_niagara");

        Assert.Equal("11", r.Point);
        Assert.True(r.HasSurvey);
        Assert.Equal("grind_175_baza_niagara", r.SurveyLastName);
        Assert.Equal("grind_175_baza_niagara", r.SurveyQuery);
        // point@survey ≡ survey.point (top-down).
        Assert.Equal("grind_175_baza_niagara.11", r.StationQuery);
    }

    [Fact]
    public void Parse_reverses_multi_component_survey_path()
    {
        // `p@inner.outer` (bottom-up) ≡ `outer.inner.p` (top-down).
        var r = StationRef.Parse("p@inner.outer");

        Assert.Equal("p", r.Point);
        Assert.Equal(new[] { "outer", "inner" }, r.SurveyPathTopDown);
        Assert.Equal("outer.inner", r.SurveyQuery);
        Assert.Equal("outer.inner.p", r.StationQuery);
    }

    [Fact]
    public void Parse_bare_token_has_no_survey()
    {
        var r = StationRef.Parse("grind_wg_superior_meandru");

        Assert.False(r.HasSurvey);
        Assert.Null(r.SurveyQuery);
        Assert.Equal("grind_wg_superior_meandru", r.Point);
        Assert.Equal("grind_wg_superior_meandru", r.StationQuery);
    }

    [Fact]
    public void PointWithoutMark_strips_join_mark()
    {
        var r = StationRef.Parse("17:end@scrapA");
        Assert.Equal("17:end", r.Point);
        Assert.Equal("17", r.PointWithoutMark);
        Assert.Equal("scrapA", r.SurveyLastName);
    }

    [Theory]
    [InlineData(0, ReferencePart.Point)]   // on the first digit of "11"
    [InlineData(1, ReferencePart.Point)]
    [InlineData(2, ReferencePart.Point)]   // on the '@' itself
    [InlineData(3, ReferencePart.Survey)]  // first char after '@'
    [InlineData(8, ReferencePart.Survey)]
    public void ClassifyClick_picks_point_vs_survey_by_offset(int offset, ReferencePart expected)
    {
        var (part, _) = StationRef.ClassifyClick("11@grind", offset);
        Assert.Equal(expected, part);
    }

    [Fact]
    public void ClassifyClick_whole_token_when_no_at()
    {
        var (part, r) = StationRef.ClassifyClick("plainsurvey", 3);
        Assert.Equal(ReferencePart.Whole, part);
        Assert.Equal("plainsurvey", r.Point);
    }
}
