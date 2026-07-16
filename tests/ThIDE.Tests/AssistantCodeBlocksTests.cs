using System.Linq;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

/// <summary>The fence scanner that turns an assistant answer into prose/code segments (CAP-03).</summary>
public class AssistantCodeBlocksTests
{
    [Fact]
    public void ProseOnly_IsOneProseSegment()
    {
        var segs = AssistantCodeBlocks.Parse("Just some words, no fences.");
        var seg = Assert.Single(segs);
        Assert.False(seg.IsCode);
        Assert.Equal("Just some words, no fences.", seg.Text);
    }

    [Fact]
    public void EmptyOrNull_IsNoSegments()
    {
        Assert.Empty(AssistantCodeBlocks.Parse(""));
        Assert.Empty(AssistantCodeBlocks.Parse(null));
    }

    [Fact]
    public void FencedBlock_CapturesLanguageAndContentsWithoutFences()
    {
        var text = "Here is a survey:\n```therion\nsurvey a\nendsurvey\n```\nAdd it where you like.";
        var segs = AssistantCodeBlocks.Parse(text);

        Assert.Collection(segs,
            s => { Assert.False(s.IsCode); Assert.Equal("Here is a survey:", s.Text); },
            s => { Assert.True(s.IsCode); Assert.Equal("therion", s.Language); Assert.Equal("survey a\nendsurvey", s.Text); },
            s => { Assert.False(s.IsCode); Assert.Equal("Add it where you like.", s.Text); });
    }

    [Fact]
    public void FenceWithoutLanguage_HasEmptyLanguage()
    {
        var segs = AssistantCodeBlocks.Parse("```\nx 1 2\n```");
        var seg = Assert.Single(segs);
        Assert.True(seg.IsCode);
        Assert.Equal("", seg.Language);
        Assert.Equal("x 1 2", seg.Text);
    }

    [Fact]
    public void UnterminatedFence_TakesTheRestAsCode()
    {
        var segs = AssistantCodeBlocks.Parse("intro\n```therion\nsurvey a");
        Assert.Collection(segs,
            s => { Assert.False(s.IsCode); Assert.Equal("intro", s.Text); },
            s => { Assert.True(s.IsCode); Assert.Equal("survey a", s.Text); });
    }

    [Fact]
    public void BlankProseBetweenBlocks_IsDropped()
    {
        var segs = AssistantCodeBlocks.Parse("```\na\n```\n\n```\nb\n```");
        Assert.Equal(2, segs.Count);
        Assert.All(segs, s => Assert.True(s.IsCode));
    }

    [Fact]
    public void CrlfIsNormalized()
    {
        var segs = AssistantCodeBlocks.Parse("p\r\n```therion\r\nsurvey a\r\n```");
        Assert.Contains(segs, s => s.IsCode && s.Text == "survey a");
    }
}
