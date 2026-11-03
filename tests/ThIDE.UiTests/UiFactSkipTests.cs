namespace ThIDE.UiTests;

// When a UI test may start the app. Plain facts: they run in every test pass, UI or not.
public class UiFactSkipTests
{
    [Fact]
    public void Skips_without_the_opt_in() =>
        Assert.NotNull(UiFactAttribute.SkipReason(uiTests: null, display: ":99", isLinux: true, isWindows: false));

    [Fact]
    public void Skips_on_windows_even_when_opted_in() =>
        // No throw-away profile there: the app would use the developer's own.
        Assert.NotNull(UiFactAttribute.SkipReason(uiTests: "1", display: null, isLinux: false, isWindows: true));

    [Fact]
    public void Skips_on_linux_without_a_display() =>
        Assert.NotNull(UiFactAttribute.SkipReason(uiTests: "1", display: "", isLinux: true, isWindows: false));

    [Theory]
    [InlineData(true)]    // Linux with a display
    [InlineData(false)]   // macOS
    public void Runs_when_opted_in_on_unix(bool isLinux) =>
        Assert.Null(UiFactAttribute.SkipReason(uiTests: "1", display: ":99", isLinux, isWindows: false));
}
