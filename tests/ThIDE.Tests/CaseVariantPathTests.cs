using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// On a case-sensitive file system (Linux) `Cave.th` and `cave.th` are two files; the document and
/// session services must not treat one as the other.
/// </summary>
public class CaseVariantPathTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static string LargeSurvey(string name)
    {
        // Big enough that reading it does not finish before the second open starts.
        var sb = new StringBuilder($"survey {name}\n  centreline\n    data normal from to length compass clino\n");
        for (int i = 0; i < 40_000; i++)
            sb.Append("    ").Append(i).Append(' ').Append(i + 1).Append(" 3.25 180.5 -2.0\n");
        sb.Append("  endcentreline\nendsurvey\n");
        return sb.ToString();
    }

    [Fact]
    public async Task Case_variant_files_open_as_two_documents_when_opened_at_once()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var dir = new TempDir();
        var upper = dir.Write("Cave.th", LargeSurvey("upper"));
        var lower = dir.Write("cave.th", LargeSurvey("lower"));
        var svc = new DocumentService(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

        await Task.WhenAll(svc.OpenFileAsync(upper), svc.OpenFileAsync(lower));

        var opened = svc.Documents.Select(d => System.IO.Path.GetFileName(d.FilePath)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(opened.Count == 2, $"expected Cave.th and cave.th open, got: {string.Join(", ", opened)}");
    }

    [Fact]
    public async Task A_case_variant_of_a_tracked_file_is_not_covered_by_the_workspace()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var dir = new TempDir();
        dir.Write("Cave.th", "survey upper\nendsurvey\n");
        var lower = dir.Write("cave.th", "survey lower\nendsurvey\n");
        var thconfig = dir.Write("main.thconfig", "encoding utf-8\nsource Cave.th\n");

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        Assert.True(await session.SetActiveThconfigAsync(thconfig));

        Assert.False(session.Covers(lower), "cave.th is not sourced by the thconfig but the workspace reports it as covered (paths compared case-insensitively)");
    }
}
