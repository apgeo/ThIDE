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
}
