// map…endmap body parsing: member ids + projection.

using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class MapMembersTests
{
    private static MapCommand ParseMap(string body)
    {
        var r = new ThParser().Parse("/p/a.th", body);
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == Core.DiagnosticSeverity.Error);
        return r.Value!.Children.OfType<MapCommand>().Single();
    }

    [Fact]
    public void Map_members_and_projection_are_captured()
    {
        var map = ParseMap("""
            map m-all-p -projection plan
                cowboys-1p # 10 13
                cowboys-2p # 5 9
                cowboys-3p # 1 4
            endmap
            """);
        Assert.Equal("m-all-p", map.Id);
        Assert.Equal("plan", map.Projection);
        Assert.Equal(new[] { "cowboys-1p", "cowboys-2p", "cowboys-3p" },
            map.Members.Select(m => m.Id));
    }

    [Fact]
    public void Map_member_spans_point_at_the_id_token()
    {
        var map = ParseMap("""
            map m
                scrapA
            endmap
            """);
        var member = map.Members.Single();
        Assert.Equal("scrapA", member.Id);
        Assert.False(member.Span.IsEmpty);
    }
}
