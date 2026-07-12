using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using ThIDE.Docking;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

// Full-layout persistence contract: the layout is described declaratively (LayoutProfile)
// and REBUILT from that description — never restored by deserializing a Dock tree (which
// does not render in Dock.Avalonia 12). These tests cover the profile round-trip, the
// live-tree capture, the built-in presets and the factory rebuild.
public class LayoutProfileTests
{
    // ---- file round-trip -------------------------------------------------

    [Fact]
    public void Profile_round_trips_through_thlayout_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "my" + LayoutProfileFile.Extension);
        var profile = new LayoutProfile
        {
            LeftProportion = 0.21,
            LeftSections = new[]
            {
                new DockSectionState { Tools = new[] { "Workspace" }, ActiveTool = "Workspace", Proportion = 0.5 },
                new DockSectionState { Tools = new[] { "Project" }, Proportion = 0.5 },
            },
            CenterTools = new[] { "ObjectBrowser" },
            FloatWindows = new[]
            {
                new FloatWindowState { X = 10, Y = 20, Width = 800, Height = 600, Maximized = true,
                    DockableIds = new[] { "LivePreview" } },
            },
        };

        LayoutProfileFile.Save(path, profile);
        var reloaded = LayoutProfileFile.TryLoad(path);

        Assert.NotNull(reloaded);
        Assert.Equal(0.21, reloaded!.LeftProportion);
        Assert.Equal(2, reloaded.LeftSections.Count);
        Assert.Equal(new[] { "Workspace" }, reloaded.LeftSections[0].Tools);
        Assert.Equal("Workspace", reloaded.LeftSections[0].ActiveTool);
        Assert.Equal(new[] { "ObjectBrowser" }, reloaded.CenterTools);
        var f = Assert.Single(reloaded.FloatWindows);
        Assert.True(f.Maximized);
        Assert.Equal(new[] { "LivePreview" }, f.DockableIds);
    }

    [Fact]
    public void Newer_or_corrupt_layout_file_is_rejected()
    {
        using var dir = new TempDir();
        var newer = Path.Combine(dir.Path, "newer.thlayout");
        File.WriteAllText(newer, "{ \"Version\": 999 }");
        Assert.Null(LayoutProfileFile.TryLoad(newer));

        var corrupt = Path.Combine(dir.Path, "corrupt.thlayout");
        File.WriteAllText(corrupt, "not json at all");
        Assert.Null(LayoutProfileFile.TryLoad(corrupt));
    }

    [Fact]
    public void Profile_persists_inside_layout_json()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "layout.json");
        var state = LayoutState.Default with
        {
            Profile = new LayoutProfile
            {
                RightSections = new[] { new DockSectionState { Tools = new[] { "Outline", "LivePreview" } } },
            },
        };

        new JsonLayoutService(path).Save(state);
        var reloaded = new JsonLayoutService(path).Current;

        Assert.NotNull(reloaded.Profile);
        var section = Assert.Single(reloaded.Profile!.RightSections);
        Assert.Equal(new[] { "Outline", "LivePreview" }, section.Tools);
    }

    [Fact]
    public void Legacy_layout_json_without_profile_loads_with_null_profile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "layout.json");
        File.WriteAllText(path, "{ \"WindowWidth\": 1200 }");
        Assert.Null(new JsonLayoutService(path).Current.Profile);
    }

    // ---- live-tree capture -------------------------------------------------

    private static readonly HashSet<string> KnownTools = new(StringComparer.Ordinal)
    {
        "Workspace", "Project", "ObjectBrowser", "Diagnostics", "CompilerOutput",
        "GeneratedFiles", "Log", "Outline", "LivePreview", "MapViewer", "Model3DViewer",
    };

    private static Tool T(string id) => new() { Id = id, Title = id };

    private static ToolDock TD(string id, double proportion, string? active, params Tool[] tools)
    {
        var dock = new ToolDock
        {
            Id = id,
            Proportion = proportion,
            VisibleDockables = new ObservableCollection<IDockable>(tools),
        };
        dock.ActiveDockable = tools.FirstOrDefault(t => t.Id == active);
        return dock;
    }

    [Fact]
    public void Capture_reads_regions_sections_and_active_tabs()
    {
        // A split left rail (2 stacked sections), single right dock, bottom dock and a
        // tool tab in the document well — the shape the split presets produce.
        var workspace = T("Workspace");
        var project = T("Project");
        var leftRail = new ProportionalDock
        {
            Id = "LeftToolsRail",
            Orientation = Orientation.Vertical,
            Proportion = 0.2,
            VisibleDockables = new ObservableCollection<IDockable>
            {
                TD("LeftTools", 0.6, "Workspace", workspace),
                new ProportionalDockSplitter(),
                TD("LeftTools2", 0.4, "Project", project),
            },
        };
        var documents = new DocumentDock
        {
            Id = "Documents",
            VisibleDockables = new ObservableCollection<IDockable> { T("ObjectBrowser") },
        };
        documents.ActiveDockable = documents.VisibleDockables![0];
        var center = new ProportionalDock
        {
            Id = "CenterColumn",
            Orientation = Orientation.Vertical,
            Proportion = 0.58,
            VisibleDockables = new ObservableCollection<IDockable>
            {
                documents,
                new ProportionalDockSplitter(),
                TD("BottomTools", 0.3, "Log", T("Diagnostics"), T("Log")),
            },
        };
        var root = new RootDock
        {
            Id = "Root",
            VisibleDockables = new ObservableCollection<IDockable>
            {
                new ProportionalDock
                {
                    Id = "MainRow",
                    Orientation = Orientation.Horizontal,
                    VisibleDockables = new ObservableCollection<IDockable>
                    {
                        leftRail,
                        new ProportionalDockSplitter(),
                        center,
                        new ProportionalDockSplitter(),
                        TD("RightTools", 0.22, "Outline", T("Outline"), T("LivePreview")),
                    },
                },
            },
        };

        var p = LayoutProfileCapture.Capture(root, KnownTools.Contains);

        Assert.Equal(2, p.LeftSections.Count);
        Assert.Equal(new[] { "Workspace" }, p.LeftSections[0].Tools);
        Assert.Equal(new[] { "Project" }, p.LeftSections[1].Tools);
        Assert.Equal("Project", p.LeftSections[1].ActiveTool);
        Assert.Equal(0.2, p.LeftProportion);

        var right = Assert.Single(p.RightSections);
        Assert.Equal(new[] { "Outline", "LivePreview" }, right.Tools);
        Assert.Equal("Outline", right.ActiveTool);

        var bottom = Assert.Single(p.BottomSections);
        Assert.Equal(new[] { "Diagnostics", "Log" }, bottom.Tools);
        Assert.Equal("Log", bottom.ActiveTool);
        Assert.Equal(0.3, p.BottomProportion);
        Assert.Equal(0.58, p.CenterProportion);

        Assert.Equal(new[] { "ObjectBrowser" }, p.CenterTools);
        Assert.Equal("ObjectBrowser", p.CenterActiveTool);
    }

    [Fact]
    public void Capture_ignores_unknown_tools_and_nan_proportions()
    {
        var dock = TD("LeftTools", double.NaN, null, T("Workspace"), T("NotARealTool"));
        var root = new RootDock
        {
            Id = "Root",
            VisibleDockables = new ObservableCollection<IDockable>
            {
                new ProportionalDock
                {
                    Id = "MainRow",
                    Orientation = Orientation.Horizontal,
                    VisibleDockables = new ObservableCollection<IDockable>
                    {
                        dock,
                        new DocumentDock { Id = "Documents", VisibleDockables = new ObservableCollection<IDockable>() },
                    },
                },
            },
        };

        var p = LayoutProfileCapture.Capture(root, KnownTools.Contains);

        var left = Assert.Single(p.LeftSections);
        Assert.Equal(new[] { "Workspace" }, left.Tools);
        // NaN must never reach the profile (it is not JSON-serializable) — defaults apply.
        Assert.True(p.LeftProportion > 0);
    }

    // ---- presets -------------------------------------------------------------

    [Fact]
    public void Split_preset_divides_rails_into_two_or_three_sections()
    {
        var p2 = LayoutPresets.SplitSideRails(KnownTools, 2);
        Assert.Equal(2, p2.LeftSections.Count);
        Assert.Equal(2, p2.RightSections.Count);
        Assert.Single(p2.BottomSections);

        var p3 = LayoutPresets.SplitSideRails(KnownTools, 3);
        Assert.Equal(2, p3.LeftSections.Count);            // only 2 left tools exist
        Assert.Equal(3, p3.RightSections.Count);
        Assert.Equal(2, p3.BottomSections.Count);          // bottom splits side-by-side

        // Every available core tool stays reachable and none is duplicated.
        var all = p3.AllToolIds().ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Contains("Workspace", all);
        Assert.Contains("Outline", all);
        Assert.Contains("Diagnostics", all);
    }

    [Fact]
    public void Split_preset_skips_unavailable_tools()
    {
        var few = new HashSet<string>(StringComparer.Ordinal) { "Workspace", "Diagnostics", "Outline" };
        var p = LayoutPresets.SplitSideRails(few, 3);
        Assert.Single(p.LeftSections);                     // Project unavailable → one section
        Assert.Single(p.RightSections);                    // only Outline → one section
        Assert.DoesNotContain("LivePreview", p.AllToolIds());
    }

    [Fact]
    public void MultiMonitor_preset_requires_two_screens()
    {
        var one = new[] { new ScreenRect(0, 0, 1920, 1080) };
        Assert.Null(LayoutPresets.MultiMonitor(one, KnownTools));
        // No enabled preview panel to float → also null.
        var two = new[] { new ScreenRect(0, 0, 1920, 1080), new ScreenRect(1920, 0, 1920, 1080) };
        var noPreviews = new HashSet<string>(StringComparer.Ordinal) { "Workspace", "Diagnostics" };
        Assert.Null(LayoutPresets.MultiMonitor(two, noPreviews));
    }

    [Fact]
    public void MultiMonitor_preset_puts_all_previews_on_the_second_screen_when_only_two()
    {
        var screens = new[] { new ScreenRect(0, 0, 1920, 1080), new ScreenRect(1920, 0, 2560, 1440) };
        var p = LayoutPresets.MultiMonitor(screens, KnownTools);

        Assert.NotNull(p);
        var f = Assert.Single(p!.FloatWindows);
        Assert.Equal(1920, f.X);
        Assert.True(f.Maximized);
        Assert.Equal(new[] { "LivePreview", "MapViewer", "Model3DViewer" }, f.DockableIds);
        // The floated panels must not also sit in a docked section.
        Assert.DoesNotContain(p.LeftSections.Concat(p.RightSections).Concat(p.BottomSections),
            s => s.Tools.Contains("LivePreview"));
    }

    [Fact]
    public void MultiMonitor_preset_spreads_previews_over_monitors_two_and_three()
    {
        var screens = new[]
        {
            new ScreenRect(0, 0, 1920, 1080),
            new ScreenRect(1920, 0, 1920, 1080),
            new ScreenRect(3840, 0, 1920, 1080),
            new ScreenRect(-1920, 0, 1920, 1080),   // a 4th monitor is ignored
        };
        var p = LayoutPresets.MultiMonitor(screens, KnownTools);

        Assert.NotNull(p);
        Assert.Equal(2, p!.FloatWindows.Count);
        Assert.Equal(1920, p.FloatWindows[0].X);
        Assert.Equal(3840, p.FloatWindows[1].X);
        // All preview panels are dispersed, none duplicated.
        var floated = p.FloatWindows.SelectMany(w => w.DockableIds).ToList();
        Assert.Equal(new[] { "LivePreview", "MapViewer", "Model3DViewer" }, floated.OrderBy(x => x));
    }

    // ---- factory rebuild ------------------------------------------------------

    private static DockFactory NewFactory() => new(
        new WelcomeToolViewModel(), new WorkspaceExplorerToolViewModel(), new ObjectBrowserToolViewModel(),
        new DiagnosticsToolViewModel(), new CompilerOutputToolViewModel(), new GeneratedFilesToolViewModel(),
        new XviToolViewModel(), new OutlineToolViewModel(), new ProjectToolViewModel(), new LogToolViewModel(),
        new LivePreviewToolViewModel(), new MapViewerToolViewModel(), new Model3DViewerToolViewModel(),
        new StructuralGeologyToolViewModel(), new StructuralPlotToolViewModel(),
        new BlenderAnimationToolViewModel(), new SettingsToolViewModel());

    private static T? FindById<T>(IDockable node, string id) where T : class, IDockable
    {
        if (node is T hit && string.Equals(node.Id, id, StringComparison.Ordinal)) return hit;
        if (node is IDock dock && dock.VisibleDockables is { } children)
            foreach (var c in children)
                if (FindById<T>(c, id) is { } found)
                    return found;
        return null;
    }

    [Fact]
    public void BuildFromProfile_builds_a_split_left_rail_with_canonical_ids()
    {
        var factory = NewFactory();
        var profile = new LayoutProfile
        {
            LeftSections = new[]
            {
                new DockSectionState { Tools = new[] { "Workspace" }, ActiveTool = "Workspace" },
                new DockSectionState { Tools = new[] { "Project" } },
            },
            CenterTools = new[] { "ObjectBrowser" },
        };

        var root = factory.BuildFromProfile(profile);

        // First section keeps the canonical id (other features look "LeftTools" up by id).
        var first = FindById<IToolDock>(root, "LeftTools");
        var second = FindById<IToolDock>(root, "LeftTools2");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Contains(first!.VisibleDockables!, d => d.Id == "Workspace");
        Assert.Contains(second!.VisibleDockables!, d => d.Id == "Project");
        Assert.NotNull(FindById<IDocumentDock>(root, "Documents"));
        // The rail is a real stacked container, not a single dock.
        Assert.NotNull(FindById<IProportionalDock>(root, "LeftToolsRail"));
    }

    [Fact]
    public void BuildFromProfile_keeps_unplaced_default_tools_reachable()
    {
        var factory = NewFactory();
        // A profile that only places the workspace: the bottom tools, project pane and
        // object browser must still be docked somewhere (their default homes).
        var profile = new LayoutProfile
        {
            LeftSections = new[] { new DockSectionState { Tools = new[] { "Workspace" } } },
        };

        var root = factory.BuildFromProfile(profile);

        foreach (var id in new[] { "Project", "Diagnostics", "CompilerOutput", "GeneratedFiles", "Log", "ObjectBrowser" })
            Assert.True(FindById<IDockable>(root, id) is not null, $"'{id}' must remain reachable");
    }

    [Fact]
    public void BuildFromProfile_docks_floated_tools_so_they_can_be_torn_off()
    {
        var factory = NewFactory();
        // "Log" floats in this profile → the build must place it in the tree (its home
        // region) so RestoreFloatWindows can tear it off with a live float operation.
        var profile = new LayoutProfile
        {
            BottomSections = new[] { new DockSectionState { Tools = new[] { "Diagnostics" } } },
            FloatWindows = new[]
            {
                new FloatWindowState { X = 0, Y = 0, DockableIds = new[] { "Log" } },
            },
        };

        var root = factory.BuildFromProfile(profile);

        var bottom = FindById<IToolDock>(root, "BottomTools");
        Assert.NotNull(bottom);
        Assert.Contains(bottom!.VisibleDockables!, d => d.Id == "Log");
    }

    [Fact]
    public void BuildFromProfile_drops_unknown_and_duplicate_tools()
    {
        var factory = NewFactory();
        var profile = new LayoutProfile
        {
            LeftSections = new[]
            {
                new DockSectionState { Tools = new[] { "Workspace", "NoSuchTool", "Workspace" } },
            },
        };

        var root = factory.BuildFromProfile(profile);
        var left = FindById<IToolDock>(root, "LeftTools");

        Assert.NotNull(left);
        Assert.Single(left!.VisibleDockables!.Where(d => d.Id == "Workspace"));
        Assert.DoesNotContain(left.VisibleDockables!, d => d.Id == "NoSuchTool");
    }

    [Fact]
    public void Capture_of_a_rebuilt_profile_is_stable()
    {
        var factory = NewFactory();
        var profile = new LayoutProfile
        {
            LeftSections = new[]
            {
                new DockSectionState { Tools = new[] { "Workspace" }, ActiveTool = "Workspace", Proportion = 0.5 },
                new DockSectionState { Tools = new[] { "Project" }, ActiveTool = "Project", Proportion = 0.5 },
            },
            BottomSections = new[]
            {
                new DockSectionState { Tools = new[] { "Diagnostics", "CompilerOutput", "GeneratedFiles", "Log" }, ActiveTool = "Diagnostics" },
            },
            CenterTools = new[] { "ObjectBrowser" },
        };

        factory.BuildFromProfile(profile);
        var captured = factory.CaptureProfile();

        Assert.Equal(2, captured.LeftSections.Count);
        Assert.Equal(new[] { "Workspace" }, captured.LeftSections[0].Tools);
        Assert.Equal(new[] { "Project" }, captured.LeftSections[1].Tools);
        var bottom = Assert.Single(captured.BottomSections);
        Assert.Equal(new[] { "Diagnostics", "CompilerOutput", "GeneratedFiles", "Log" }, bottom.Tools);
        Assert.Equal(new[] { "ObjectBrowser" }, captured.CenterTools);
    }
}
