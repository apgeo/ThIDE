using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class AutomationOutputTests
{
    [Theory]
    [InlineData("caves/a.th:12: error: bad", "caves/a.th", 12)]
    [InlineData("warning [main.th:3] unknown station", "main.th", 3)]
    [InlineData("C:/x/b.thconfig(7): oops", "/x/b.thconfig", 7)]
    public void Rows_naming_a_position_carry_it(string text, string file, int line)
    {
        var row = AutomationSession.ParseRow(text, isError: true);

        Assert.EndsWith(file, row.File);
        Assert.Equal(line, row.Line);
    }

    [Fact]
    public void Plain_rows_carry_no_position()
    {
        Assert.Null(AutomationSession.ParseRow("compiling 3 files", false).File);
        Assert.Null(AutomationSession.ParseRow("took 12:30", false).File);
    }
}
