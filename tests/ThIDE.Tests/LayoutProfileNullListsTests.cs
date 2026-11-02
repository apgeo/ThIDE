using System;
using System.IO;
using ThIDE.Docking;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

// A hand-edited or foreign *.thlayout file may carry explicit JSON nulls where the profile
// expects lists. Loading such a file must either be rejected by TryLoad or produce a profile
// the factory can build a tree from; it must never throw on the apply path.
public class LayoutProfileNullListsTests
{
    private static DockFactory NewFactory() => new(
        new WelcomeToolViewModel(), new WorkspaceExplorerToolViewModel(), new ObjectBrowserToolViewModel(),
        new DiagnosticsToolViewModel(), new CompilerOutputToolViewModel(), new GeneratedFilesToolViewModel(),
        new XviToolViewModel(), new OutlineToolViewModel(), new ProjectToolViewModel(), new LogToolViewModel(),
        new LivePreviewToolViewModel(), new MapViewerToolViewModel(), new Model3DViewerToolViewModel(),
        new StructuralGeologyToolViewModel(), new StructuralPlotToolViewModel(),
        new StructuralStereonetToolViewModel(),
        new AssistantToolViewModel(), new BlenderAnimationToolViewModel(),
        new SettingsToolViewModel());

    [Theory]
    [InlineData("""{"Version":1,"LeftSections":null}""")]
    [InlineData("""{"Version":1,"LeftSections":[null]}""")]
    [InlineData("""{"Version":1,"LeftSections":[{"Tools":null}]}""")]
    [InlineData("""{"Version":1,"CenterTools":null}""")]
    [InlineData("""{"Version":1,"FloatWindows":[{"DockableIds":null}]}""")]
    public void Load_layout_with_null_lists_is_rejected_or_normalized(string json)
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "edited" + LayoutProfileFile.Extension);
        File.WriteAllText(path, json);

        var profile = LayoutProfileFile.TryLoad(path);
        if (profile is null) return; // rejected up front: the caller shows a notification

        var ex = Record.Exception(() => NewFactory().BuildFromProfile(profile));
        Assert.True(ex is null,
            $"TryLoad accepted the file but BuildFromProfile threw {ex?.GetType().Name}: {ex?.Message}");
    }
}
