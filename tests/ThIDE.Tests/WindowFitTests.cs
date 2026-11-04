using System.Text.RegularExpressions;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>Fitting windows onto a 1366x768 laptop: size clamping, degenerate sizes, toast anchoring.</summary>
public class WindowFitTests
{
    // 1366x768 with a 40 px taskbar.
    private const double AvailW = 1366, AvailH = 728;

    [Theory]
    [InlineData(1300, 800, 560, 1300, 704)]   // main window: taller than the work area
    [InlineData(1040, 820, 600, 1040, 704)]   // link survey
    [InlineData(1040, 780, 560, 1040, 704)]   // new trip
    [InlineData(1240, 680, 300, 1240, 680)]   // replace: already fits, untouched
    public void Clamp_ShrinksTallWindowsIntoTheWorkArea(double w, double h, double minH, double expW, double expH)
    {
        var (cw, ch) = WindowFit.Clamp(w, h, 500, minH, AvailW, AvailH);
        Assert.Equal(expW, cw);
        Assert.Equal(expH, ch);
    }

    [Fact]
    public void Clamp_CountsTheTitleBarAgainstTheWorkArea()
    {
        // 768 screen, 29 px title bar: client 715 so that frame + margin fit.
        var (_, h) = WindowFit.Clamp(1300, 800, 900, 560, 1366, 768, chromeHeight: 29);
        Assert.Equal(715, h);
    }

    [Fact]
    public void FirstRunSize_CountsTheTitleBarAgainstTheWorkArea()
    {
        var (_, h) = WindowFit.FirstRunSize(900, 560, 1366, 768, chromeHeight: 29);
        Assert.Equal(652 - 29, h);
    }

    [Fact]
    public void Clamp_NeverGoesBelowTheWindowMinimum()
    {
        var (w, h) = WindowFit.Clamp(1300, 800, 900, 560, 800, 500);
        Assert.Equal(900, w);
        Assert.Equal(560, h);
    }

    [Fact]
    public void Clamp_NeverGrowsAWindow()
    {
        var (w, h) = WindowFit.Clamp(600, 400, 300, 300, 3840, 2160);
        Assert.Equal(600, w);
        Assert.Equal(400, h);
    }

    [Fact]
    public void Nudge_PullsAWindowBackInsideTheWorkArea()
    {
        // Window 1300x704 placed at y=58 on a 728-high area starting at y=0: bottom edge at 762.
        var (x, y) = WindowFit.Nudge(20, 58, 1300, 704, 0, 0, AvailW, AvailH);
        Assert.Equal(20, x);
        Assert.Equal(24, y);
        Assert.True(y + 704 <= AvailH);
    }

    [Fact]
    public void FirstRunSize_TakesMostOfTheWorkAreaAndRespectsTheMinimum()
    {
        var (w, h) = WindowFit.FirstRunSize(900, 560, AvailW, AvailH);
        Assert.Equal(1161, w);
        Assert.Equal(618, h);
        Assert.True(h <= AvailH);

        var (w2, h2) = WindowFit.FirstRunSize(900, 560, 800, 500);
        Assert.Equal(900, w2);
        Assert.Equal(560, h2);
    }

    [Theory]
    [InlineData(325, 50, false)]      // seen after two full-screen toggles
    [InlineData(325, 800, false)]
    [InlineData(1300, 400, false)]
    [InlineData(900, 560, true)]
    [InlineData(1300, 800, true)]
    [InlineData(double.NaN, 800, false)]
    [InlineData(1300, double.PositiveInfinity, false)]
    public void IsUsableSize_RejectsDegenerateSizes(double w, double h, bool expected)
        => Assert.Equal(expected, WindowFit.IsUsableSize(w, h, 900, 560));

    [Fact]
    public void ToastBottomMargin_ClearsTheStatusBar()
        => Assert.Equal(26 + 8, WindowFit.ToastBottomMargin(700, 728, 26));

    [Fact]
    public void ToastBottomMargin_AddsHowFarTheWindowRunsPastTheWorkArea()
        => Assert.Equal(58 + 26 + 8, WindowFit.ToastBottomMargin(786, 728, 26));

    [Fact]
    public void ToastBottomMargin_ClearsATabStripTallerThanTheStatusBar()
        => Assert.Equal(58 + 8, WindowFit.ToastBottomMargin(700, 728, 26, 58));

    [Fact]
    public void ToastBottomMargin_IgnoresATabStripLowerThanTheStatusBar()
        => Assert.Equal(26 + 8, WindowFit.ToastBottomMargin(700, 728, 26, 10));

    [Fact]
    public void TabStripClearance_IsTheDistanceFromTheWindowBottomToTheTopOfTheLowestStripUnderTheToasts()
    {
        // 1366x768 window: a 28 px strip along the bottom of the right-hand dock, above a 26 px status bar.
        var rightStrip = new Avalonia.Rect(1000, 686, 366, 28);
        Assert.Equal(768 - 686, WindowFit.TabStripClearance(768, 1366 - 400, new[] { rightStrip }));
    }

    [Fact]
    public void TabStripClearance_SkipsStripsLeftOfTheToastsAndStripsHighUp()
    {
        var bottomDockStrip = new Avalonia.Rect(250, 686, 600, 28);  // ends left of the toast column
        var topSectionStrip = new Avalonia.Rect(1000, 90, 366, 28);  // upper half of the window
        Assert.Equal(0, WindowFit.TabStripClearance(768, 966, new[] { bottomDockStrip, topSectionStrip }));
    }

    [Fact]
    public void TabStripClearance_PicksTheLowestOfSeveralStrips()
    {
        var upper = new Avalonia.Rect(1000, 400, 366, 28);
        var lower = new Avalonia.Rect(1000, 686, 366, 28);
        Assert.Equal(82, WindowFit.TabStripClearance(768, 966, new[] { upper, lower }));
    }

    // ---- wizard dialogs: Esc must close them, and typing must work on open ----

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ThIDE.sln not found above the test output");
    }

    private static string Views(string file) => Path.Combine(RepoRoot(), "ThIDE", "Views", file);

    [Theory]
    [InlineData("NewFileWindow")]
    [InlineData("NewProjectWindow")]
    [InlineData("NewTripWindow")]
    [InlineData("LinkSurveyWindow")]
    [InlineData("PasteShotsWindow")]
    [InlineData("RenumberStationsWindow")]
    [InlineData("ScaffoldProjectWindow")]
    [InlineData("QuickExportWindow")]
    [InlineData("NormalizeProjectWindow")]
    [InlineData("SyncMapsWindow")]
    [InlineData("EditPreviewWindow")]
    public void WizardWindows_CancelButtonIsTheEscapeButton(string window)
    {
        var xaml = File.ReadAllText(Views(window + ".axaml"));
        var cancel = Regex.Matches(xaml, @"<Button\b[^>]*>", RegexOptions.Singleline)
            .Select(m => m.Value).Single(b => b.Contains("Click=\"OnCancel\"", StringComparison.Ordinal));
        Assert.Contains("IsCancel=\"True\"", cancel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SuggestEquatesWindow", "OnClose")]
    [InlineData("BookmarksWindow", "OnCloseClick")]
    [InlineData("ReferencesWindow", "OnCloseClick")]
    public void ListWindows_CloseButtonIsTheEscapeButton(string window, string handler)
    {
        var xaml = File.ReadAllText(Views(window + ".axaml"));
        var close = Regex.Matches(xaml, @"<Button\b[^>]*>", RegexOptions.Singleline)
            .Select(m => m.Value).Single(b => b.Contains($"Click=\"{handler}\"", StringComparison.Ordinal));
        Assert.Contains("IsCancel=\"True\"", close, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchBuildWindow_CloseButtonIsTheEscapeButton()
    {
        var xaml = File.ReadAllText(Views("BatchBuildWindow.axaml"));
        var close = Regex.Matches(xaml, @"<Button\b[^>]*>", RegexOptions.Singleline)
            .Select(m => m.Value).Single(b => b.Contains("Click=\"OnClose\"", StringComparison.Ordinal));
        Assert.Contains("IsCancel=\"True\"", close, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NewFileWindow")]
    [InlineData("NewProjectWindow")]
    [InlineData("NewTripWindow")]
    [InlineData("LinkSurveyWindow")]
    [InlineData("PasteShotsWindow")]
    [InlineData("RenumberStationsWindow")]
    [InlineData("ScaffoldProjectWindow")]
    [InlineData("QuickExportWindow")]
    [InlineData("NormalizeProjectWindow")]
    public void WizardWindows_FocusTheirFirstInputWhenOpened(string window)
    {
        Assert.Contains("x:Name=\"FirstInput\"", File.ReadAllText(Views(window + ".axaml")), StringComparison.Ordinal);
        Assert.Contains("FocusOnOpen(\"FirstInput\")", File.ReadAllText(Views(window + ".axaml.cs")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MainWindow")]
    [InlineData("NewTripWindow")]
    [InlineData("LinkSurveyWindow")]
    [InlineData("PreviewWindow")]
    [InlineData("ReplaceWindow")]
    [InlineData("PreferencesWindow")]
    [InlineData("RelationalMapWindow")]
    [InlineData("SearchWindow")]
    [InlineData("ScaffoldProjectWindow")]
    [InlineData("PasteShotsWindow")]
    [InlineData("NewFileWindow")]
    public void TallWindows_ClampThemselvesToTheWorkArea(string window)
    {
        var code = File.ReadAllText(Views(window + ".axaml.cs"));
        Assert.True(code.Contains("ClampOnOpen()", StringComparison.Ordinal) || code.Contains("ClampToWorkArea()", StringComparison.Ordinal),
            window + " does not clamp itself to the work area");
    }

    [Fact]
    public void MainWindow_HasAMinimumSize()
    {
        var xaml = File.ReadAllText(Views("MainWindow.axaml"));
        Assert.Contains("MinWidth=\"900\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"560\"", xaml, StringComparison.Ordinal);
    }
}
