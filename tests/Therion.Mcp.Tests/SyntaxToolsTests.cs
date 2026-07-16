// CAP-06.1: describe_command. Pure — the grammar model, not a project — so no workspace is needed.
// The point of these tests is that help matches what the validator enforces: the `-fmt` cases below
// are the ones a model gets wrong from memory (there is no `lox` format; the keyword is `loch`).

using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class SyntaxToolsTests
{
    [Fact]
    public void Describes_a_block_command_with_its_terminator_and_inherited_options()
    {
        var result = new SyntaxTools().DescribeCommand("map");

        Assert.True(result.Ok);
        var doc = result.Data!;
        Assert.Equal("map", doc.Keyword);
        Assert.True(doc.IsBlock);
        Assert.Equal("endmap", doc.Terminator);
        Assert.Contains("endmap", doc.Syntax);
        Assert.Contains(doc.Positional, p => p.Name == "id" && p.Required);

        // -projection is map's own; -title comes from the inherited data-object set. Both are real
        // syntax, so both must show without the caller knowing about option-set inheritance.
        Assert.Contains(doc.Options, o => o.Name == "projection");
        Assert.Contains(doc.Options, o => o.Name == "title");
    }

    [Fact]
    public void The_thbook_citation_comes_from_the_same_index_search_thbook_uses()
    {
        var doc = new SyntaxTools().DescribeCommand("equate").Data!;
        var hit = Assert.Single(new ThbookTools().SearchThbook("equate").Data!.Hits, h => h.Term == "equate");

        Assert.Equal(hit.Citation, doc.ThbookCitation);
    }

    [Fact]
    public void Export_model_lists_the_real_formats_so_lox_is_not_guessed()
    {
        var result = new SyntaxTools().DescribeCommand("export model");

        Assert.True(result.Ok);
        var fmt = Assert.Single(result.Data!.Options, o => o.Name == "fmt");
        var values = Assert.Single(fmt.Values).Values;

        Assert.NotNull(values);
        // A .lox file is what `-fmt loch` writes; `lox` itself is not a format the compiler accepts.
        Assert.Contains("loch", values!);
        Assert.DoesNotContain("lox", values!);
        Assert.Contains("format", fmt.Aliases);
    }

    [Fact]
    public void Export_model_says_which_fmt_writes_a_lox_file()
    {
        // The question is always asked by the file wanted ("a .lox 3D model"), so the answer has to be
        // reachable from the extension — listing `loch` among twelve formats leaves the leap to a guess.
        var doc = new SyntaxTools().DescribeCommand("export model").Data!;

        Assert.NotNull(doc.Notes);
        Assert.Contains(".lox is written by -fmt loch", doc.Notes);
        Assert.Contains("not a file extension", doc.Notes);
    }

    [Fact]
    public void The_note_is_per_type_because_the_trap_is_per_type()
    {
        // sql/csv write their own names, so there is nothing to warn about — padding help with an
        // irrelevant caveat trains the reader to skip it.
        Assert.Null(new SyntaxTools().DescribeCommand("export database").Data!.Notes);

        // A map, though, has its own trap and not the model's: `survex` writes .3d and `3d` is not a
        // map format, while .lox is not a map output at all.
        var map = new SyntaxTools().DescribeCommand("export map").Data!;
        Assert.Contains(".3d is written by -fmt survex", map.Notes);
        Assert.DoesNotContain("loch", map.Notes);
    }

    [Fact]
    public void Export_options_are_per_type()
    {
        var model = new SyntaxTools().DescribeCommand("export model");
        var map = new SyntaxTools().DescribeCommand("export map");

        // -wall-source is model-only; -proj is map-only. One shared `export` doc would document both
        // for both, which is exactly the guessing this tool exists to stop.
        Assert.Contains(model.Data!.Options, o => o.Name == "wall-source");
        Assert.DoesNotContain(model.Data.Options, o => o.Name == "proj");
        Assert.Contains(map.Data!.Options, o => o.Name == "proj");
        Assert.DoesNotContain(map.Data.Options, o => o.Name == "wall-source");

        var mapFmt = Assert.Single(map.Data.Options, o => o.Name == "fmt");
        var mapFormats = Assert.Single(mapFmt.Values).Values!;
        Assert.Contains("svg", mapFormats);
        Assert.DoesNotContain("loch", mapFormats);   // a model format, not a map one
    }

    [Fact]
    public void Bare_export_answers_with_its_types_rather_than_one_arbitrary_variant()
    {
        var result = new SyntaxTools().DescribeCommand("export");

        Assert.True(result.Ok);
        var doc = result.Data!;
        var type = Assert.Single(doc.Positional);
        Assert.Equal("type", type.Name);
        Assert.Contains("model", type.Values!);
        Assert.Contains("atlas", type.Values!);
        Assert.Contains("export model", doc.Variants);
        Assert.Empty(doc.Options);   // options are per-type; naming any here would be a guess
    }

    [Fact]
    public void Enum_values_and_ranges_reach_the_caller()
    {
        var vthreshold = new SyntaxTools().DescribeCommand("vthreshold");
        var value = Assert.Single(vthreshold.Data!.Positional, p => p.Name == "value");
        Assert.Equal("[0, 90]", value.Range);

        var data = new SyntaxTools().DescribeCommand("data");
        var style = Assert.Single(data.Data!.Positional, p => p.Name == "style");
        Assert.Contains("normal", style.Values!);
        Assert.Contains("topofil", style.Values!);
    }

    [Fact]
    public void Alias_and_whitespace_both_resolve()
    {
        Assert.True(new SyntaxTools().DescribeCommand("centerline").Ok);      // US spelling alias
        Assert.True(new SyntaxTools().DescribeCommand("  export   model ").Ok);
        Assert.True(new SyntaxTools().DescribeCommand("MAP").Ok);
    }

    [Fact]
    public void Context_disambiguates_and_a_wrong_one_is_a_clean_error()
    {
        var inCentreline = new SyntaxTools().DescribeCommand("cs", "Centreline");
        Assert.True(inCentreline.Ok);

        var wrongBlock = new SyntaxTools().DescribeCommand("fix", "Th2TopLevel");
        Assert.False(wrongBlock.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, wrongBlock.Error!.Code);

        var badName = new SyntaxTools().DescribeCommand("fix", "NotABlock");
        Assert.False(badName.Ok);
        Assert.Contains("Centreline", badName.Error!.Message);   // lists the valid contexts
    }

    [Fact]
    public void An_unmodeled_command_says_so_instead_of_inventing_syntax()
    {
        var result = new SyntaxTools().DescribeCommand("zzzznotacommand");

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
        // The model is incomplete, so a miss must not be reported as "no such command".
        Assert.Contains("does not yet cover", result.Error.Message);
        Assert.Contains("map", result.Error.Message);   // the known list comes along

        var empty = new SyntaxTools().DescribeCommand("   ");
        Assert.False(empty.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, empty.Error!.Code);
    }

    [Fact]
    public void Every_command_it_claims_to_know_actually_describes()
    {
        var names = SyntaxTools.KnownCommands;
        Assert.Contains("map", names);
        Assert.Contains("export", names);
        Assert.Contains("export model", names);
        Assert.Equal(names, names.Distinct().ToList());

        // A list that names a command it cannot describe is worse than no list.
        var tools = new SyntaxTools();
        foreach (var name in names)
            Assert.True(tools.DescribeCommand(name).Ok, $"{name} is listed but does not describe.");
    }
}
