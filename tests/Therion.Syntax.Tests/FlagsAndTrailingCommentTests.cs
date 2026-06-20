// Covers the Measurements-view enablers added to the .th parser:
//  - `flags ...` inside a centreline becomes a FlagsCommand (not a phantom DataRow)
//  - an inline `# ...` after a data row is captured as DataRow.TrailingComment
//  - both survive a TherionWriter round-trip.

using System.Collections.Immutable;
using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class FlagsAndTrailingCommentTests
{
    private const string Src =
        "survey demo\n" +
        "  centreline\n" +
        "    data normal from to length compass clino\n" +
        "      0 1 12.5 0 -5    # start of trip\n" +
        "    flags duplicate\n" +
        "      1 2 8.0 90 0\n" +
        "    flags not duplicate\n" +
        "  endcentreline\n" +
        "endsurvey\n";

    private static ImmutableArray<TherionNode> AllNodes(TherionNode node)
    {
        var b = ImmutableArray.CreateBuilder<TherionNode>();
        void Walk(TherionNode n)
        {
            b.Add(n);
            var children = n switch
            {
                TherionFile f  => f.Children,
                BlockCommand b => b.Children,
                _              => ImmutableArray<TherionNode>.Empty,
            };
            foreach (var c in children) Walk(c);
        }
        Walk(node);
        return b.ToImmutable();
    }

    [Fact]
    public void Flags_line_parses_as_FlagsCommand_not_DataRow()
    {
        var file = new ThParser().Parse("demo.th", Src).Value;
        var flags = AllNodes(file).OfType<FlagsCommand>().ToList();

        Assert.Equal(2, flags.Count);
        Assert.Equal(new[] { "duplicate" }, flags[0].Tokens);
        Assert.Equal(new[] { "not", "duplicate" }, flags[1].Tokens);
    }

    [Fact]
    public void Flags_lines_do_not_produce_phantom_data_rows()
    {
        var file = new ThParser().Parse("demo.th", Src).Value;
        var rows = AllNodes(file).OfType<DataRow>().ToList();

        // Only the two real shots, never a "flags duplicate" row.
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.Values.Contains("flags"));
    }

    [Fact]
    public void Inline_comment_is_captured_as_trailing_comment()
    {
        var file = new ThParser().Parse("demo.th", Src).Value;
        var first = AllNodes(file).OfType<DataRow>().First();

        Assert.Equal("start of trip", first.TrailingComment);
        // The comment must not leak into the data values.
        Assert.Equal(new[] { "0", "1", "12.5", "0", "-5" }, first.Values);
    }

    [Fact]
    public void Writer_round_trips_flags_and_trailing_comment()
    {
        var file = new ThParser().Parse("demo.th", Src).Value;
        var text = new TherionWriter().Write(file);

        Assert.Contains("flags duplicate", text);
        Assert.Contains("flags not duplicate", text);
        Assert.Contains("0 1 12.5 0 -5 # start of trip", text);
    }
}
