using ThIDE.Services;

namespace ThIDE.Tests;

public class GoToLineInputTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 42 ", 42)]
    [InlineData("120", 120)]
    public void ALineInsideTheFile_IsAccepted(string text, int expected)
    {
        Assert.True(GoToLineInput.TryResolve(text, 120, out var line, out var problem));
        Assert.Equal(expected, line);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("99999")]
    [InlineData("-3")]
    public void ALineOutsideTheFile_IsRefusedWithTheRange(string text)
    {
        Assert.False(GoToLineInput.TryResolve(text, 120, out _, out var problem));
        Assert.NotNull(problem);
        Assert.Contains("120", problem);
    }

    [Fact]
    public void TextThatIsNoNumber_IsRefusedWithAMessage()
    {
        Assert.False(GoToLineInput.TryResolve("twelve", 120, out _, out var problem));
        Assert.Contains("twelve", problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingEntered_IsACancelAndSaysNothing(string? text)
    {
        Assert.False(GoToLineInput.TryResolve(text, 120, out _, out var problem));
        Assert.Null(problem);
    }
}
