using System.Text;
using Therion.Build;

namespace Therion.Build.Tests;

/// <summary>
/// The model-export config is the project's thconfig made to run from a temporary folder: absolute
/// paths, no exports or shell commands of its own, one model export. Files are invented in memory.
/// </summary>
public class ModelExportConfigTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\proj" : "/proj";

    private static string P(params string[] parts) => Path.GetFullPath(Path.Combine([Root, .. parts]));

    private static string Create(Dictionary<string, string> files, string config = "cave.thconfig")
        => ModelExportConfig.Create(P(config), path => files.TryGetValue(path, out var text) ? text : null);

    private static Dictionary<string, string> Files(params (string Path, string Text)[] files)
        => files.ToDictionary(f => P(f.Path), f => f.Text);

    [Fact]
    public void Source_paths_become_absolute_and_one_model_export_is_added()
    {
        var text = Create(Files(("cave.thconfig", "source cave.th\nsource data/east.th # the east branch\n")));

        Assert.Contains($"source \"{P("cave.th")}\"\n", text);
        Assert.Contains($"source \"{P("data", "east.th")}\" # the east branch\n", text);
        Assert.EndsWith("export model -fmt loch -o \"model.lox\"\n", text);
    }

    [Fact]
    public void Absolute_and_quoted_paths_are_kept_as_written_targets()
    {
        string elsewhere = OperatingSystem.IsWindows() ? @"D:\other\x.th" : "/other/x.th";
        var text = Create(Files(("cave.thconfig", $"source {elsewhere}\nsource \"my survey/a b.th\"\n")));

        Assert.Contains($"source \"{elsewhere}\"\n", text);
        Assert.Contains($"source \"{P("my survey", "a b.th")}\"\n", text);
    }

    [Fact]
    public void Exports_system_commands_and_layouts_are_left_out()
    {
        var text = Create(Files(("cave.thconfig", string.Join('\n',
            "encoding utf-8",
            "source cave.th",
            "layout plan",
            "  scale 1 500",
            "  code metapost",
            "    def x = 1;",
            "  endcode",
            "endlayout",
            "lookup depth",
            "  0 [255 0 0]",
            "endlookup",
            "select cave@cave",
            "cs UTM34",
            "system \"rm -rf somewhere\"",
            "export map -fmt pdf \\",
            "  -o cave.pdf -layout plan",
            "export model -o cave.lox",
            ""))));

        Assert.DoesNotContain("layout", text);
        Assert.DoesNotContain("lookup", text);
        Assert.DoesNotContain("scale", text);
        Assert.DoesNotContain("system", text);
        Assert.DoesNotContain("rm -rf", text);
        Assert.DoesNotContain("cave.pdf", text);
        Assert.DoesNotContain("cave.lox", text);
        Assert.Contains("encoding utf-8\n", text);
        Assert.Contains("select cave@cave\n", text);
        Assert.Contains("cs UTM34\n", text);
        Assert.Single(text.Split('\n'), l => l.StartsWith("export", StringComparison.Ordinal));
    }

    [Fact]
    public void An_inputted_config_is_inlined_with_paths_relative_to_itself()
    {
        var text = Create(Files(
            ("cave.thconfig", "input common/sources.thc\n"),
            ("common/sources.thc", "source ../cave.th\nsource local.th\nexport map -o common.pdf\n")));

        Assert.Contains($"source \"{P("cave.th")}\"\n", text);
        Assert.Contains($"source \"{P("common", "local.th")}\"\n", text);
        Assert.DoesNotContain("input", text);
        Assert.DoesNotContain("common.pdf", text);
    }

    [Fact]
    public void An_unreadable_or_cyclic_input_stays_an_absolute_input()
    {
        var text = Create(Files(
            ("cave.thconfig", "input missing.thc\ninput loop.thc\n"),
            ("loop.thc", "input cave.thconfig\nsource x.th\n")));

        Assert.Contains($"input \"{P("missing.thc")}\"\n", text);
        Assert.Contains($"input \"{P("cave.thconfig")}\"\n", text);
        Assert.Contains($"source \"{P("x.th")}\"\n", text);
    }

    [Fact]
    public void An_inline_source_block_is_kept_with_its_inputs_made_absolute()
    {
        var text = Create(Files(("cave.thconfig", string.Join('\n',
            "source",
            "  survey cave",
            "    input data/a.th",
            "    export map",
            "  endsurvey",
            "endsource",
            "source b.th",
            ""))));

        Assert.Contains("source\n  survey cave\n", text);
        Assert.Contains($"input \"{P("data", "a.th")}\"\n", text);
        // inside the block, "export" is survey data's business, not a config export
        Assert.Contains("    export map\n", text);
        Assert.Contains("endsource\n", text);
        Assert.Contains($"source \"{P("b.th")}\"\n", text);
    }

    [Fact]
    public void Bytes_outside_ascii_survive_and_added_paths_are_utf8()
    {
        // A Latin-1 comment byte the rewriter must not touch, and a UTF-8 folder name.
        string latin1Comment = "# pe\u0219tera \u00e9\n";
        string utf8Name = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes("pe\u0219ter\u0103.th"));
        var text = Create(Files(("cave.thconfig", latin1Comment + "source " + utf8Name + "\n")));

        Assert.StartsWith(latin1Comment, text);
        string expected = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(P("pe\u0219ter\u0103.th")));
        Assert.Contains($"source \"{expected}\"\n", text);
    }

    [Fact]
    public void Crlf_line_ends_and_a_missing_final_newline_are_handled()
    {
        var text = Create(Files(("cave.thconfig", "encoding utf-8\r\nsource cave.th\r\nexport model")));

        Assert.Contains("encoding utf-8\r\n", text);
        Assert.Contains($"source \"{P("cave.th")}\"\n", text);
        Assert.EndsWith("export model -fmt loch -o \"model.lox\"\n", text);
        Assert.Single(text.Split('\n'), l => l.StartsWith("export", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_config_throws()
        => Assert.Throws<FileNotFoundException>(() => Create(Files()));
}
