// Tests for the #@ directive line grammar (DirectiveParser): type extraction and the
// argument tokenizer (blanks/commas, quotes, `_`/`undefined`/empty → undefined).

using System.Linq;
using Therion.Syntax.Directives;

namespace Therion.Syntax.Tests.Directives;

public class DirectiveParserTests
{
    private static TherionDirective Parse(string line)
    {
        Assert.True(DirectiveParser.TryParse(line, "/d/a.th", 1, 0, out var d), $"not a directive: {line}");
        return d;
    }

    private static string?[] Args(string line) => Parse(line).Args.Select(a => a.Value).ToArray();

    // ---- detection --------------------------------------------------------------------------

    [Theory]
    [InlineData("#@region")]
    [InlineData("   #@region")]           // indented
    [InlineData("\t#@region 'x'")]        // tab-indented
    [InlineData("data 1 2 #@region 'x'")] // trailing comment directive
    public void Directive_comments_are_recognized(string line) =>
        Assert.True(DirectiveParser.TryParse(line, "/d/a.th", 1, 0, out _));

    [Theory]
    [InlineData("# region")]              // plain comment, not #@
    [InlineData("# @region")]             // space between # and @
    [InlineData("region 'x'")]            // not a comment
    [InlineData("data 1 2 3")]
    [InlineData("\"a#@b\"")]              // #@ inside a Therion string is not a comment
    [InlineData("#@")]                    // no type
    public void Non_directives_are_ignored(string line) =>
        Assert.False(DirectiveParser.TryParse(line, "/d/a.th", 1, 0, out _));

    [Fact]
    public void Directive_type_is_lowercased_but_raw_is_preserved()
    {
        var d = Parse("#@ReGion 'x'");
        Assert.Equal("region", d.Type);
        Assert.Equal("ReGion", d.RawType);
    }

    // ---- argument tokenizing (the spec examples) --------------------------------------------

    [Fact]
    public void Spec_example_space_separated()
    {
        Assert.Equal(new[] { "Sample title", "red", "5" }, Args("#@region 'Sample title' red 5"));
    }

    [Fact]
    public void Spec_example_comma_separated_with_trailing_comma_is_equivalent()
    {
        Assert.Equal(new[] { "Sample title", "red", "5" }, Args("#@region 'Sample title', red, 5,"));
    }

    [Fact]
    public void Spec_example_trailing_blanks_drop_missing_params()
    {
        Assert.Equal(new[] { "Sample title" }, Args("#@region 'Sample title' "));
    }

    [Theory]
    [InlineData("#@x \"double quoted\"", "double quoted")]
    [InlineData("#@x 'single quoted'", "single quoted")]
    public void Quoted_strings_are_single_args(string line, string expected) =>
        Assert.Equal(new[] { expected }, Args(line));

    [Fact]
    public void Empty_slot_between_commas_is_undefined()
    {
        var args = Args("#@x a,,b");
        Assert.Equal(new[] { "a", null, "b" }, args);
    }

    [Fact]
    public void Blank_between_commas_is_undefined()
    {
        Assert.Equal(new[] { "a", null, "b" }, Args("#@x a, , b"));
    }

    [Theory]
    [InlineData("#@x _")]
    [InlineData("#@x undefined")]
    [InlineData("#@x UNDEFINED")]
    public void Underscore_and_undefined_keyword_are_undefined(string line)
    {
        Assert.Single(Args(line));
        Assert.Null(Args(line)[0]);
    }

    [Fact]
    public void Quoted_undefined_keyword_is_a_defined_value()
    {
        Assert.Equal(new[] { "undefined" }, Args("#@x 'undefined'"));
    }

    [Fact]
    public void Leading_comma_makes_a_leading_undefined()
    {
        Assert.Equal(new string?[] { null, "a" }, Args("#@x ,a"));
    }

    [Fact]
    public void Multiple_blanks_are_one_separator()
    {
        Assert.Equal(new[] { "a", "b" }, Args("#@x    a     b"));
    }

    [Fact]
    public void Region_with_no_args_has_empty_arg_list()
    {
        Assert.Empty(Parse("#@region").Args);
    }

    [Fact]
    public void ArgValue_out_of_range_is_null()
    {
        var d = Parse("#@region 'title'");
        Assert.Equal("title", d.ArgValue(0));
        Assert.Null(d.ArgValue(1));
        Assert.Null(d.ArgValue(-1));
    }
}
