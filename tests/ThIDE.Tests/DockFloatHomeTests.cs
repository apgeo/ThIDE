// Floating a panel and putting it back, on the dock model alone (no host windows are opened):
//   * a float from the menu remembers the dock it left, which enables Dock.Avalonia's "Dock" (it asks
//     for an OriginalOwner), and opens a step away from where the panel was;
//   * "Dock" (PinDockable) moves the panel back into that dock and closes its emptied window;
//   * a panel dragged out of the window floats where it was dropped, and can still go back;
//   * when its own dock went with it, the panel goes back to a dock on the same side.

using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using ThIDE.Docking;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;
using Xunit;

namespace ThIDE.Tests;

[Collection(nameof(DockDragStateCollection))]
public class DockFloatHomeTests
{
    private static (DockFactory Factory, IRootDock Root) NewLayout()
    {
        var factory = new DockFactory(
            new WelcomeToolViewModel(), new WorkspaceExplorerToolViewModel(), new ObjectBrowserToolViewModel(),
            new DiagnosticsToolViewModel(), new CompilerOutputToolViewModel(), new GeneratedFilesToolViewModel(),
            new XviToolViewModel(), new OutlineToolViewModel(), new ProjectToolViewModel(), new LogToolViewModel(),
            new LivePreviewToolViewModel(), new MapViewerToolViewModel(), new Model3DViewerToolViewModel(),
            new StructuralGeologyToolViewModel(), new StructuralPlotToolViewModel(),
            new StructuralStereonetToolViewModel(),
            new AssistantToolViewModel(), new BlenderAnimationToolViewModel(),
            new SettingsToolViewModel(),
            layoutPath: System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thide-float-" + System.Guid.NewGuid().ToString("N")));
        var root = factory.BuildFromProfile(new LayoutProfile
        {
            BottomSections = new[] { new DockSectionState { Tools = new[] { "Diagnostics", "Log" } } },
        });
        factory.InitLayout(root);
        factory.HostWindowLocator = new Dictionary<string, System.Func<IHostWindow?>>();   // model only
        return (factory, root);
    }

    private static IDockable Tool(IDockable node, string id) => Find(node, id) ?? throw new System.Exception(id);

    private static IDockable? Find(IDockable node, string id)
    {
        if (node.Id == id && node is not IDock) return node;
        if (node is IDock dock && dock.VisibleDockables is { } list)
            foreach (var c in list)
                if (Find(c, id) is { } hit) return hit;
        return null;
    }

    [Fact]
    public void A_float_remembers_its_dock_and_opens_a_step_away()
    {
        var (factory, root) = NewLayout();
        var log = Tool(root, "Log");
        var bottom = log.Owner;

        factory.FloatDockable(log);

        var window = Assert.Single(root.Windows!);
        Assert.Null(Find(root, "Log"));                 // left the main window
        Assert.Same(bottom, log.OriginalOwner);          // "Dock" is enabled on it
        Assert.Equal(DockFactory.FloatCascadeStep, window.X);
        Assert.Equal(DockFactory.FloatCascadeStep, window.Y);
    }

    [Fact]
    public void Dock_puts_a_floated_panel_back_and_closes_its_window()
    {
        var (factory, root) = NewLayout();
        var log = Tool(root, "Log");
        var bottom = (IDock)log.Owner!;
        factory.FloatDockable(log);

        factory.PinDockable(log);   // the menu's "Dock"

        Assert.Same(bottom, log.Owner);
        Assert.Contains(log, bottom.VisibleDockables!);
        Assert.Null(log.OriginalOwner);                  // "Auto Hide" is offered again
        Assert.Empty(root.Windows!);
        Assert.False(factory.IsDockablePinned(log));     // docked, not auto-hidden
    }

    [Fact]
    public void A_dragged_out_panel_floats_where_it_was_dropped_and_can_go_back()
    {
        var (factory, root) = NewLayout();
        var log = Tool(root, "Log");
        var bottom = log.Owner;

        DockDragState.Set(true);
        try { factory.FloatDockable(log); }
        finally { DockDragState.Set(false); }

        var window = Assert.Single(root.Windows!);
        Assert.Equal(0, window.X);                       // no step: the drop point (none here: 0)
        Assert.Same(bottom, log.OriginalOwner);
    }

    [Fact]
    public void A_panel_whose_dock_went_with_it_goes_back_to_the_same_side()
    {
        var (factory, root) = NewLayout();
        var diagnostics = Tool(root, "Diagnostics");
        var log = Tool(root, "Log");
        factory.FloatDockable(diagnostics);
        factory.FloatDockable(log);                      // the bottom dock is empty now and collapses

        factory.PinDockable(log);

        var owner = Assert.IsAssignableFrom<IToolDock>(log.Owner);
        Assert.Equal(Alignment.Bottom, owner.Alignment);
        Assert.NotNull(Find(root, "Log"));
        Assert.Single(root.Windows!);                    // Diagnostics still floats
    }

    [Fact]
    public void A_second_float_steps_past_a_window_already_there()
    {
        var taken = new[] { (32.0, 32.0) };
        Assert.Equal((64.0, 64.0), DockFactory.CascadeFloatPosition(0, 0, taken));
        Assert.Equal((132.0, 82.0), DockFactory.CascadeFloatPosition(100, 50, taken));
        Assert.True(double.IsNaN(DockFactory.CascadeFloatPosition(double.NaN, 0, taken).X));
    }
}

/// <summary>Tests that set <see cref="DockDragState"/> (static) run one at a time.</summary>
[CollectionDefinition(nameof(DockDragStateCollection), DisableParallelization = true)]
public class DockDragStateCollection { }
