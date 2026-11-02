using System.Text.RegularExpressions;

namespace ThIDE.Tests;

/// <summary>
/// The Replace in Files window's markup: Enter in the Find or Replace-with box fires the window's
/// default button, so that button must be the non-destructive Find All (as in Find in Files),
/// never Replace All, which rewrites every matching file in the scope.
/// </summary>
public class ReplaceWindowDefaultButtonTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ThIDE.sln not found above the test output");
    }

    private static string ButtonWithAutomationId(string xaml, string id)
    {
        foreach (Match m in Regex.Matches(xaml, @"<Button\b[^>]*>", RegexOptions.Singleline))
            if (m.Value.Contains($"AutomationProperties.AutomationId=\"{id}\"", StringComparison.Ordinal))
                return m.Value;
        throw new InvalidOperationException($"No Button with AutomationId {id} in the markup");
    }

    [Fact]
    public void EnterInTheFindBox_RunsFindAll_NotReplaceAll()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ThIDE", "Views", "ReplaceWindow.axaml"));
        var findAll = ButtonWithAutomationId(xaml, "Replace.FindAll");
        var replaceAll = ButtonWithAutomationId(xaml, "Replace.ReplaceAll");

        Assert.DoesNotContain("IsDefault=\"True\"", replaceAll, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsDefault=\"True\"", findAll, StringComparison.OrdinalIgnoreCase);
    }
}
