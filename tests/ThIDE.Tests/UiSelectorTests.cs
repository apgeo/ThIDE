using ThIDE.Services.UiAutomation;

namespace ThIDE.Tests;

public class UiSelectorTests
{
    private static UiElementInfo El(string role = "Button", string name = "", string id = "", string type = "Button",
        string? text = null, params string[] states) =>
        new(role, type, name, id, text, states, null);

    [Theory]
    [InlineData("#Toolbar.OpenFile", true)]
    [InlineData("id:Toolbar.OpenFile", true)]
    [InlineData("#Toolbar.Open", false)]
    public void Id_terms_match_exactly(string selector, bool expected) =>
        Assert.Equal(expected, UiSelector.Parse(selector).MatchesStep(0, El(id: "Toolbar.OpenFile")));

    [Fact]
    public void Bare_text_is_an_exact_name()
    {
        var s = UiSelector.Parse("3D Viewer");
        Assert.True(s.MatchesStep(0, El(name: "3D Viewer")));
        Assert.False(s.MatchesStep(0, El(name: "3D Viewer (float)")));
    }

    [Fact]
    public void Terms_joined_by_semicolon_must_all_hold()
    {
        var s = UiSelector.Parse("role:tabitem;name~:^3D;state:selected");
        Assert.True(s.MatchesStep(0, El(role: "TabItem", name: "3D Viewer", states: "selected")));
        Assert.False(s.MatchesStep(0, El(role: "TabItem", name: "3D Viewer")));
        Assert.False(s.MatchesStep(0, El(role: "Button", name: "3D Viewer", states: "selected")));
    }

    [Fact]
    public void Type_and_text_terms()
    {
        var s = UiSelector.Parse("type:TextEditor;text~:source demo");
        Assert.True(s.MatchesStep(0, El(type: "TextEditor", text: "encoding utf-8\nsource demo.th")));
        Assert.False(s.MatchesStep(0, El(type: "TextEditor", text: null)));
    }

    [Fact]
    public void Descendant_steps_and_index_are_parsed()
    {
        var s = UiSelector.Parse("#Viewer3D.View >> role:Text;name:demo.lox @2");
        Assert.Equal(2, s.StepCount);
        Assert.Equal(2, s.Index);
        Assert.True(s.MatchesStep(0, El(id: "Viewer3D.View")));
        Assert.True(s.MatchesStep(1, El(role: "Text", name: "demo.lox")));
    }

    [Fact]
    public void Name_may_contain_colons_and_semicolons_only_via_regex()
    {
        // "name:" takes the rest of the term verbatim, including ':'.
        Assert.True(UiSelector.Parse("name:Color by:").MatchesStep(0, El(name: "Color by:")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#a >>  >> #b")]
    public void Empty_selectors_are_rejected(string selector) =>
        Assert.Throws<FormatException>(() => UiSelector.Parse(selector));
}
