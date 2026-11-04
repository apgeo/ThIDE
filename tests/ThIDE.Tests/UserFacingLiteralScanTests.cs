// A ratchet on English literals handed straight to user-facing APIs in the app's C# code. The scan is
// deliberately cheap (one line at a time, three shapes), so it misses literals built elsewhere; what it
// does catch may only go down. The baseline lists what is still English, file by file.

using System.Text.RegularExpressions;

namespace ThIDE.Tests;

public sealed class UserFacingLiteralScanTests
{
    private static readonly Regex[] Shapes =
    {
        // Header = "Clear Recent Files", Status = $"Saved {name}", …
        new(@"\b(Header|Title|Text|Content|PlaceholderText|Watermark|Status|StatusText|Summary|Message)\s*=\s*\$?""[^""]*[A-Za-z]{3}"),
        // new MessageDialog("Rename Symbol", …)
        new(@"new\s+(MessageDialog|ConfirmDialog|InputDialog)\(\s*\$?"""),
        // _notifications.Warning("Scrap created", …) — the log (_log.Info) stays English on purpose.
        new(@"[Nn]otifications?\??\.(Info|Warning|Error|Success)\(\s*\$?"""),
    };

    // Still English (internal ids among them: DockFactory ids, UiAutomationTools MCP titles, ViewLocator).
    // Lower a number when you localize a site; never raise one — use Tr.Get and a resx key instead.
    private static readonly Dictionary<string, int> Baseline = new(StringComparer.Ordinal)
    {
        ["ThIDE/Docking/DockFactory.cs"] = 6,
        ["ThIDE/Editor/TherionTextEditor.axaml.cs"] = 4,
        ["ThIDE/Services/UiAutomation/UiAutomationTools.cs"] = 12,
        ["ThIDE/ViewLocator.cs"] = 1,
        ["ThIDE/ViewModels/AssistantViewModel.cs"] = 1,
        ["ThIDE/ViewModels/BatchBuildViewModel.cs"] = 1,
        ["ThIDE/ViewModels/BuildViewModel.cs"] = 7,
        ["ThIDE/ViewModels/DataAnalyticsViewModel.cs"] = 2,
        ["ThIDE/ViewModels/MainWindowViewModel.cs"] = 16,
        ["ThIDE/ViewModels/MapViewerViewModel.cs"] = 1,
        ["ThIDE/ViewModels/Model3DViewerViewModel.cs"] = 6,
        ["ThIDE/ViewModels/ReplaceInFilesViewModel.cs"] = 4,
        ["ThIDE/ViewModels/SearchViewModel.cs"] = 2,
        ["ThIDE/ViewModels/SettingsViewModel.cs"] = 2,
        ["ThIDE/Views/AboutWindow.cs"] = 2,
        ["ThIDE/Views/CoordinateConverterWindow.cs"] = 2,
        ["ThIDE/Views/DeclinationWindow.cs"] = 1,
        ["ThIDE/Views/DiffDialog.cs"] = 1,
        ["ThIDE/Views/RenamePreviewWindow.cs"] = 1,
        ["ThIDE/Views/UnitConverterWindow.cs"] = 1,
    };

    [Fact]
    public void No_new_English_literal_reaches_a_user_facing_API()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ThIDE.sln"))) root = root.Parent;
        Assert.NotNull(root);

        var over = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root!.FullName, "ThIDE"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal)) continue;
            var hits = File.ReadLines(file)
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith('*'))
                .Where(l => Shapes.Any(s => s.IsMatch(l)))
                .ToList();
            int allowed = Baseline.GetValueOrDefault(rel);
            if (hits.Count > allowed)
                over.Add($"{rel}: {hits.Count} (baseline {allowed})\n    " + string.Join("\n    ", hits.Select(h => h.Trim())));
        }
        Assert.True(over.Count == 0, "English literals passed to user-facing APIs — localize them with Tr.Get:\n" + string.Join("\n", over));
    }

    // A literal text attribute in XAML ({l:Loc Key} is the rule): Header="Name", ToolTip.Tip="Sort by", …
    private static readonly Regex XamlLiteral =
        new(@"\b(Header|Text|ToolTip\.Tip|Content|Watermark|PlaceholderText|Title)=""(?<v>[^""{]*[A-Za-z]{3}[^""]*)""");

    // Not words to translate: file formats, line-ending names, sample values and product names.
    private static readonly HashSet<string> XamlAllowed = new(StringComparer.Ordinal)
    {
        "KML…", "GeoJSON…", "GPX…", "CSV…", "Markdown…", "HTML…", "LaTeX…",
        "LF (Linux, macOS)", "CRLF (Windows)", "survey.station", "thconfig", "quick-export", "MCP", "DevTools",
        "http://127.0.0.1:1234/v1", "qwen3-coder-30b-a3b-instruct", "geo", "plane, geo", "e.g. geo",
    };

    [Fact]
    public void No_English_literal_text_in_XAML()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ThIDE.sln"))) root = root.Parent;
        Assert.NotNull(root);

        var bad = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root!.FullName, "ThIDE"), "*.axaml", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal)) continue;
            foreach (Match m in XamlLiteral.Matches(File.ReadAllText(file)))
                if (!XamlAllowed.Contains(m.Groups["v"].Value)) bad.Add($"{rel}: {m.Value}");
        }
        Assert.True(bad.Count == 0, "English literals in XAML — use {l:Loc Key}:\n" + string.Join("\n", bad));
    }
}
