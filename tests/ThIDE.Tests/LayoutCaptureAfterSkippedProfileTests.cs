using System;
using System.Linq;
using ThIDE.Docking;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

// Safe-mode startup: when the previous run ended uncleanly the shell builds the default
// layout and never calls RestoreFloatWindows. The continuous autosave must still describe
// the LIVE tree after that, otherwise the skipped (possibly bad) saved profile is written
// back unchanged on every autosave and re-applied on the next launch.
public class LayoutCaptureAfterSkippedProfileTests
{
    private sealed class FixedLayoutService : ILayoutService
    {
        public FixedLayoutService(LayoutState state) => Current = state;
        public LayoutState Current { get; private set; }
        public event EventHandler? LayoutChanged;
        public void Save(LayoutState state) { Current = state; LayoutChanged?.Invoke(this, EventArgs.Empty); }
    }

    private static DockFactory NewFactory(ILayoutService layout) => new(
        new WelcomeToolViewModel(), new WorkspaceExplorerToolViewModel(), new ObjectBrowserToolViewModel(),
        new DiagnosticsToolViewModel(), new CompilerOutputToolViewModel(), new GeneratedFilesToolViewModel(),
        new XviToolViewModel(), new OutlineToolViewModel(), new ProjectToolViewModel(), new LogToolViewModel(),
        new LivePreviewToolViewModel(), new MapViewerToolViewModel(), new Model3DViewerToolViewModel(),
        new StructuralGeologyToolViewModel(), new StructuralPlotToolViewModel(),
        new StructuralStereonetToolViewModel(),
        new AssistantToolViewModel(), new BlenderAnimationToolViewModel(),
        new SettingsToolViewModel(), layout);

    [Fact]
    public void Autosave_after_a_skipped_profile_captures_the_live_default_layout()
    {
        // The saved profile splits the left rail in two; the default layout has one left section.
        var saved = new LayoutProfile
        {
            LeftSections = new[]
            {
                new DockSectionState { Tools = new[] { "Workspace" }, ActiveTool = "Workspace" },
                new DockSectionState { Tools = new[] { "Project" } },
            },
        };
        var layoutService = new FixedLayoutService(new LayoutState { Profile = saved });
        var factory = NewFactory(layoutService);

        // Safe-mode launch: the saved profile is skipped, the default tree is built, and the
        // float-window restore step is not run at all.
        factory.ResetToDefault();

        var captured = factory.CaptureLayoutState(layoutService.Current);

        Assert.True(!ReferenceEquals(saved, captured.Profile),
            "autosave kept the skipped saved profile instead of capturing the live layout");
        Assert.Single(captured.Profile!.LeftSections);
    }
}
