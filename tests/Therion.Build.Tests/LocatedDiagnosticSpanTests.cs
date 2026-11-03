using Therion.Build.Automation;
using Therion.Core;

namespace Therion.Build.Tests;

/// <summary>
/// A diagnostic that names a file and a line must carry a span the IDE can follow. The editor's
/// navigation, squiggle and overview-ruler paths all skip a span whose <see cref="SourceSpan.IsEmpty"/>
/// is true (zero length), so a located diagnostic needs a non-zero extent.
/// </summary>
public class LocatedDiagnosticSpanTests
{
    private readonly HeuristicTherionOutputParser _parser = new();

    [Fact]
    public void File_line_problem_matcher_gives_a_navigable_span()
    {
        var d = ProblemMatchers.Match("file-line", "caves/a.th:12:3: warning: odd shot", false, "/c", "lint")!;

        Assert.Equal(12, d.Span.Start.Line);
        Assert.False(d.Span.IsEmpty, "a diagnostic at a known file:line must not have an empty (non-navigable) span");
    }

    [Fact]
    public void Therion_problem_matcher_gives_a_navigable_span()
    {
        var d = ProblemMatchers.Match("therion", "therion: error -- a.th [3] -- station does not exist -- E1", true, "/c", "build")!;

        Assert.Equal(3, d.Span.Start.Line);
        Assert.False(d.Span.IsEmpty, "a diagnostic at a known file:line must not have an empty (non-navigable) span");
    }

    [Fact]
    public void Native_therion_error_line_classifies_to_a_navigable_span()
    {
        var line = _parser.Classify("therion: error -- a.th [3] -- station does not exist -- E1", isStderr: true);

        Assert.NotNull(line.Span);
        Assert.Equal(3, line.Span!.Value.Start.Line);
        Assert.False(line.Span.Value.IsEmpty, "a located compiler line must not have an empty (non-navigable) span");
    }

    [Fact]
    public void File_line_output_classifies_to_a_navigable_span()
    {
        var line = _parser.Classify("foo/bar.th:42: warning -- something", isStderr: false);

        Assert.NotNull(line.Span);
        Assert.Equal(42, line.Span!.Value.Start.Line);
        Assert.False(line.Span.Value.IsEmpty, "a located compiler line must not have an empty (non-navigable) span");
    }
}
