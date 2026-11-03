using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests.Schema;

public class InterleavedDataOrderTests
{
    private static ImmutableArray<Diagnostic> Parse(string dataLine) =>
        new ThParser().Parse("/rules/a.th",
            $"survey s\n centreline\n{dataLine}\n endcentreline\nendsurvey\n").Diagnostics;

    private static bool HasOrderProblem(ImmutableArray<Diagnostic> d) =>
        d.Any(x => x.Code.Value == DiagnosticCodes.InterleavedMix || x.Code.Value == DiagnosticCodes.DuplicateReading);

    [Theory]
    [InlineData("data diving from to length bearing depth")]                   // depth is interleaved-only
    [InlineData("data normal station length bearing gradient")]                // interleaved, no newline
    [InlineData("data normal station newline count bearing gradient")]         // count after newline
    [InlineData("data normal from to length fromcount tocount bearing gradient")] // length with counts
    public void Orders_the_compiler_rejects_are_flagged(string dataLine)
    {
        var d = Parse(dataLine);
        Assert.True(HasOrderProblem(d),
            "no InterleavedMix/DuplicateReading for: " + dataLine + " | got: " + string.Join("; ", d.Select(x => x.Code.Value)));
    }
}
