// Dock builds a panel's view more than once (tab switch, float, a layout swap tearing the old tree
// down), and for a while those views sit outside any window. Avalonia keeps one process-wide group
// manager for radio buttons outside a window, so a GroupName couples every such instance: buttons of two
// views bound to the same view-model flag uncheck each other through it without end. That froze View ▸
// Layout ▸ Restore Default Layout while Structural Geology was open. Radio buttons in the views a panel
// can host group as siblings of their panel instead; a top-level window is built once and may name groups.

using System.Text.RegularExpressions;

namespace ThIDE.Tests;

public class DockViewRadioGroupTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ThIDE.sln not found above the test output");
    }

    [Fact]
    public void Radio_buttons_in_panel_views_carry_no_group_name()
    {
        var views = Path.Combine(RepoRoot(), "ThIDE", "Views");
        var offenders = Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).EndsWith("Window.axaml", StringComparison.Ordinal))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"<RadioButton\b[^>]*>", RegexOptions.Singleline)
                .Where(m => Regex.IsMatch(m.Value, @"\bGroupName\s*="))
                .Select(m => $"{Path.GetRelativePath(views, f)}: {m.Value.Split('\n')[0].Trim()}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "radio buttons with a GroupName in a view a panel can host (instances outside a window share the group): " +
            string.Join("; ", offenders));
    }
}
