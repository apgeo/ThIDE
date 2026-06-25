using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using TherionProc.Services;

namespace TherionProc.Tests;

// Back/forward navigation history is the caret's trail, like a normal editor (#6):
// every line the cursor rests on is a stop, not just highlighted-term/reference jumps.
public class NavigationHistoryTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewService() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    private static SourceSpan At(string file, int line) =>
        new(file, new SourceLocation(line, 1), new SourceLocation(line, 1), line, 0);

    [Fact]
    public void Keyboard_moves_to_new_lines_are_recorded_as_stops()
    {
        var svc = NewService();
        var f = "C:/proj/a.th";

        svc.ReportCaret(At(f, 1), isTermNavigation: false);
        Assert.False(svc.CanGoBack); // only one stop so far

        svc.ReportCaret(At(f, 2), isTermNavigation: false); // adjacent keyboard move still counts
        svc.ReportCaret(At(f, 7), isTermNavigation: false);

        Assert.True(svc.CanGoBack);
        Assert.False(svc.CanGoForward);
    }

    [Fact]
    public void Same_line_column_moves_coalesce()
    {
        var svc = NewService();
        var f = "C:/proj/a.th";

        svc.ReportCaret(At(f, 3), isTermNavigation: false);
        // Typing along the same line (different columns) must not pile up stops.
        svc.ReportCaret(new SourceSpan(f, new SourceLocation(3, 5), new SourceLocation(3, 5), 12, 0), false);
        svc.ReportCaret(new SourceSpan(f, new SourceLocation(3, 9), new SourceLocation(3, 9), 16, 0), false);

        Assert.False(svc.CanGoBack); // still a single stop on line 3
    }

    [Fact]
    public void Term_navigation_and_back_forward_do_not_pollute_the_trail()
    {
        var svc = NewService();
        var f = "C:/proj/a.th";

        svc.ReportCaret(At(f, 1), isTermNavigation: false);
        svc.ReportCaret(At(f, 4), isTermNavigation: false);
        // Shift+F12 occurrence cycling lands on line 40 but must NOT add a stop.
        svc.ReportCaret(At(f, 40), isTermNavigation: true);

        Assert.True(svc.CanGoBack);
        Assert.False(svc.CanGoForward); // line 40 was not appended
    }

    [Fact]
    public async Task Going_back_then_moving_drops_the_forward_branch()
    {
        using var dir = new TempDir();
        var file = dir.Write("a.th", "survey s\nendsurvey\n");

        var svc = NewService();
        await svc.OpenFileAsync(file); // so history navigation can find the open document

        svc.ReportCaret(At(file, 1), isTermNavigation: false);
        svc.ReportCaret(At(file, 2), isTermNavigation: false);
        Assert.True(svc.CanGoBack);

        await svc.GoBackAsync();
        Assert.True(svc.CanGoForward);  // we stepped back, forward is now available

        // Moving to a new line after stepping back discards the forward entry.
        svc.ReportCaret(At(file, 9), isTermNavigation: false);
        Assert.False(svc.CanGoForward);
    }
}
