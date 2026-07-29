// Regression tests for GitHub issue #1 "Does not recognize the default extension".
// Therion defaults an omitted include extension to `.th` (ThBook: "Default extension is
// `.th' and may be omitted"), so `input B` / `source B` must resolve to `B.th`.
// SourceGraph.DependencySites applies that default at the single resolution chokepoint;
// these lock the behavior in — including the guards that an explicit extension is never
// altered and that the raw-token accessor keeps showing the path verbatim.

using System.IO;
using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class SourceGraphResolutionTests
{
    private static TherionFile ParseTh(string path, string text)
        => new ThParser().Parse(path, text).Value!;

    private static TherionFile ParseThconfig(string path, string text)
        => new ThconfigParser().Parse(path, text).Value!;

    private static string Full(params string[] parts)
        => Path.GetFullPath(Path.Combine(parts));

    private static List<string> ResolvedDeps(TherionFile file, string parentPath)
        => SourceGraph.Dependencies(file, parentPath).Select(Path.GetFullPath).ToList();

    [Fact]
    public void Input_without_extension_resolves_to_dot_th()
    {
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input B\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath);

        Assert.Contains(Full(dir, "B.th"), deps);   // the issue #1 fix
        Assert.DoesNotContain(Full(dir, "B"), deps); // never the extensionless literal
    }

    [Fact]
    public void Input_with_explicit_th_is_not_double_appended()
    {
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input B.th\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath);

        Assert.Contains(Full(dir, "B.th"), deps);
        Assert.DoesNotContain(Full(dir, "B.th.th"), deps);
    }

    [Fact]
    public void Input_with_th2_extension_is_preserved()
    {
        // A `.th2` scrap include carries its own extension — the default must not touch it.
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input plan.th2\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath);

        Assert.Contains(Full(dir, "plan.th2"), deps);
        Assert.DoesNotContain(Full(dir, "plan.th2.th"), deps);
    }

    [Fact]
    public void Input_without_extension_in_subdir_resolves_to_dot_th()
    {
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input caves/B\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath);

        Assert.Contains(Full(dir, "caves", "B.th"), deps);
    }

    [Fact]
    public void Thconfig_source_without_extension_resolves_to_dot_th()
    {
        var cfgPath = Full("proj", "thconfig");
        var dir = Path.GetDirectoryName(cfgPath)!;
        var file = ParseThconfig(cfgPath, "source cave\n");

        var deps = ResolvedDeps(file, cfgPath);

        Assert.Contains(Full(dir, "cave.th"), deps);
    }

    [Fact]
    public void Raw_dependency_tokens_stay_verbatim()
    {
        // The token accessor is what the UI shows "as written" — it must NOT gain the default
        // extension; only the resolution layer (Dependencies/DependencySites) does.
        var file = ParseThconfig("x.thconfig", "source cave\n");

        Assert.Contains("cave", SourceGraph.DependencyTokens(file));
        Assert.DoesNotContain("cave.th", SourceGraph.DependencyTokens(file));
    }

    // ---- disk-aware resolution (the `exists` probe) --------------------------------------
    // The editor's ctrl-click has always fallen back to an on-disk `B.th2`; these lock the
    // include graph to the same candidate order — literal name, then `.th`, then `.th2`,
    // Therion's own thinput open sequence — so the two can never disagree again.

    private static List<string> ResolvedDeps(TherionFile file, string parentPath, string[] existing)
    {
        var set = existing.Select(Path.GetFullPath).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        return SourceGraph.Dependencies(file, parentPath, set.Contains).Select(Path.GetFullPath).ToList();
    }

    [Fact]
    public void Probe_resolves_an_extensionless_input_to_an_existing_th2()
    {
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input B\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath, new[] { Full(dir, "B.th2") });

        Assert.Contains(Full(dir, "B.th2"), deps);
        Assert.DoesNotContain(Full(dir, "B.th"), deps);
    }

    [Fact]
    public void Probe_prefers_th_over_a_th2_sibling()
    {
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input B\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath, new[] { Full(dir, "B.th"), Full(dir, "B.th2") });

        Assert.Contains(Full(dir, "B.th"), deps);
        Assert.DoesNotContain(Full(dir, "B.th2"), deps);
    }

    [Fact]
    public void Probe_prefers_the_literal_file_over_any_suffix()
    {
        // Therion opens the name as written before trying suffixes.
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input B\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath, new[] { Full(dir, "B"), Full(dir, "B.th") });

        Assert.Contains(Full(dir, "B"), deps);
        Assert.DoesNotContain(Full(dir, "B.th"), deps);
    }

    [Fact]
    public void Probe_never_falls_back_for_an_explicit_extension()
    {
        // `input B.th` names B.th; a B.th2 sibling must not satisfy it.
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input B.th\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath, new[] { Full(dir, "B.th2") });

        Assert.Contains(Full(dir, "B.th"), deps);
        Assert.DoesNotContain(Full(dir, "B.th2"), deps);
    }

    [Fact]
    public void Probe_defaults_to_th_when_no_candidate_exists()
    {
        // Diagnostics and the create-file quick-fix keep naming the canonical `.th` target.
        var surveyPath = Full("proj", "a.th");
        var dir = Path.GetDirectoryName(surveyPath)!;
        var file = ParseTh(surveyPath, "survey s\n  input B\nendsurvey\n");

        var deps = ResolvedDeps(file, surveyPath, System.Array.Empty<string>());

        Assert.Contains(Full(dir, "B.th"), deps);
    }

    [Fact]
    public void IncludeCandidates_orders_literal_then_th_then_th2()
    {
        Assert.Equal(new[] { "B", "B.th", "B.th2" }, SourceGraph.IncludeCandidates("B").ToArray());
        Assert.Equal(new[] { "B.th" }, SourceGraph.IncludeCandidates("B.th").ToArray());
        Assert.Equal(new[] { "plan.th2" }, SourceGraph.IncludeCandidates("plan.th2").ToArray());
    }
}
