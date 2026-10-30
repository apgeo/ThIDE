using System.Text;
using Therion.Cli;

namespace Therion.Cli.Tests;

// format --check/--write, import and report, on invented files in a temp folder.
public class HeadlessVerbTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-cli-verbs-").FullName;
    private readonly StringWriter _out = new(), _err = new();
    private const string Messy = "survey cave\ncentreline\nunits length meters\ndata normal from to length compass clino\n1 2 10 0 0\nendcentreline\nendsurvey\n";
    private const string Tidy = "survey cave\n  centreline\n    units length meters\n    data normal from to length compass clino\n    1 2 10 0 0\n  endcentreline\nendsurvey\n";

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string name, string text)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void Format_prints_the_reindented_file_and_keeps_every_command()
    {
        Assert.Equal(0, FormatVerb.Run(["format", Write("a.th", Messy)], _out, _err));
        Assert.Equal(Tidy, _out.ToString());
    }

    [Fact]
    public void Check_reports_drift_with_exit_1_and_changes_nothing()
    {
        var a = Write("a.th", Messy);
        Write("sub/b.th", Tidy);
        Write(".git/c.th", Messy);   // dot folders are not the project's sources

        Assert.Equal(1, FormatVerb.Run(["format", "--check", _root], _out, _err));
        Assert.Contains("a.th", _out.ToString());
        Assert.DoesNotContain("b.th", _out.ToString());
        Assert.DoesNotContain("c.th", _out.ToString());
        Assert.Equal(Messy, File.ReadAllText(a));

        File.WriteAllText(a, Tidy);
        Assert.Equal(0, FormatVerb.Run(["format", "--check", _root], _out, _err));
    }

    [Fact]
    public void Write_keeps_a_latin1_file_latin1()
    {
        var p = Path.Combine(_root, "l.th");
        File.WriteAllBytes(p, Encoding.Latin1.GetBytes("encoding iso8859-1\nsurvey café\ncentreline\nendcentreline\nendsurvey\n"));

        Assert.Equal(0, FormatVerb.Run(["format", "--write", "--indent", "tab", p], _out, _err));

        var bytes = File.ReadAllBytes(p);
        Assert.Contains((byte)0xE9, bytes);
        Assert.Contains("\n\tcentreline\n", Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public void Format_usage_errors()
    {
        Assert.Equal(2, FormatVerb.Run(["format"], _out, _err));
        Assert.Equal(2, FormatVerb.Run(["format", "--check", "--write", _root], _out, _err));
        Assert.Equal(2, FormatVerb.Run(["format", Path.Combine(_root, "missing.th")], _out, _err));
        Assert.Equal(2, FormatVerb.Run(["format", "--indent", "x", _root], _out, _err));
    }

    [Fact]
    public void A_file_with_parse_errors_is_skipped_not_rewritten_and_fails_the_check()
    {
        var p = Write("bad.th", "survey a\ncentreline\n");
        Assert.Equal(1, FormatVerb.Run(["format", "--check", p], _out, _err));
        Assert.Contains("skipped", _err.ToString());
        Assert.Equal(0, FormatVerb.Run(["format", "--write", p], _out, _err));
        Assert.Equal("survey a\ncentreline\n", File.ReadAllText(p));
    }

    [Fact]
    public void Import_a_shot_table_to_a_new_file_and_never_overwrite()
    {
        var csv = Write("trip.csv", "from,to,length,compass,clino\n1,2,3.5,10,-2\n2,3,4,20,1\n");
        var target = Path.Combine(_root, "trip.th");

        Assert.Equal(0, ImportVerb.Run(["import", csv, "--out", target], _out, _err));
        var th = File.ReadAllText(target);
        Assert.Contains("survey trip", th);
        Assert.Contains("1 2 3.5 10 -2", th);

        Assert.Equal(1, ImportVerb.Run(["import", csv, "--out", target], _out, _err));
        Assert.Equal(th, File.ReadAllText(target));
    }

    [Fact]
    public void Import_options_and_warnings()
    {
        var dump = Write("dump.txt", "3.4 120 -5\n3.4 120.2 -5.1\n3.41 120.1 -5\n1 40 10\n");
        Assert.Equal(0, ImportVerb.Run(["import", dump, "--format", "csv", "--start", "A1", "--survey", "d"], _out, _err));
        Assert.Contains("A1 A2", _out.ToString());
        Assert.Contains("A2 -", _out.ToString());
        Assert.Contains("warning:", _err.ToString());

        Assert.Equal(2, ImportVerb.Run(["import", dump], _out, _err));                       // .txt needs --format
        Assert.Equal(2, ImportVerb.Run(["import", dump, "--format", "csv", "--merge", "1"], _out, _err));
        Assert.Equal(2, ImportVerb.Run(["import", dump, "--format", "csv", "--columns", "from to"], _out, _err));
        Assert.Equal(2, ImportVerb.Run(["import", dump, "--format", "csv", "--survey", "two words"], _out, _err));
    }

    [Fact]
    public void Import_survex()
    {
        var svx = Write("c.svx", "*begin c\n1 2 10.0 90 0\n*end c\n");
        Assert.Equal(0, ImportVerb.Run(["import", svx], _out, _err));
        Assert.Contains("survey", _out.ToString());
    }

    [Fact]
    public async Task Report_writes_html()
    {
        Write("cave.th", Tidy);
        var cfg = Write("cave.thconfig", "source cave.th\n");
        var html = Path.Combine(_root, "out", "report.html");

        Assert.Equal(0, await ReportVerb.RunAsync(["report", cfg, "--out", html, "--title", "Invented"], _out, _err));
        var text = File.ReadAllText(html);
        Assert.StartsWith("<!doctype html>", text);
        Assert.Contains("Invented", text);

        Assert.Equal(2, await ReportVerb.RunAsync(["report"], _out, _err));
        Assert.Equal(2, await ReportVerb.RunAsync(["report", Path.Combine(_root, "nope.th")], _out, _err));
    }
}
