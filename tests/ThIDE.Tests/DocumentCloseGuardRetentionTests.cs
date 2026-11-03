using System.Reflection;
using ThIDE.Docking;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

/// <summary>
/// The close-confirmation token for a dirty document must be consumed when the close goes through,
/// whichever branch the user picked. The Save branch clears the dirty flag before the re-close, and
/// the factory must still drop its reference to the now-closed document.
/// </summary>
public class DocumentCloseGuardRetentionTests
{
    private static DockFactory NewFactory() => new(
        new WelcomeToolViewModel(new WelcomeViewModel()),
        new WorkspaceExplorerToolViewModel(new WorkspaceExplorerViewModel()),
        new ObjectBrowserToolViewModel(new ObjectBrowserViewModel()),
        new DiagnosticsToolViewModel(new DiagnosticsViewModel()),
        new CompilerOutputToolViewModel(new BuildViewModel()),
        new GeneratedFilesToolViewModel(new BuildViewModel()),
        new XviToolViewModel(new XviReferencesViewModel()),
        new OutlineToolViewModel(new OutlineViewModel()),
        new ProjectToolViewModel(new ProjectDashboardViewModel(), new SurveyTreeViewModel(), new ProjectAuditViewModel(), new DataAnalyticsViewModel(), new LeadsViewModel(), new TodoScanViewModel(), new ProjectMetadataViewModel(), new MediaManagerViewModel()),
        new LogToolViewModel(new LogViewModel()),
        new LivePreviewToolViewModel(new LivePreviewViewModel()),
        new MapViewerToolViewModel(new MapViewerViewModel()),
        new Model3DViewerToolViewModel(new Model3DViewerViewModel()),
        new StructuralGeologyToolViewModel(new StructuralGeologyViewModel()),
        new StructuralPlotToolViewModel(new StructuralGeologyViewModel()),
        new StructuralStereonetToolViewModel(new StructuralGeologyViewModel()),
        new AssistantToolViewModel(),
        new BlenderAnimationToolViewModel(new BlenderAnimationViewModel()),
        TestProfile.SettingsTool(),
        layoutPath: TestProfile.NewPath("dock-layout.json"));

    private static IReadOnlyCollection<FileDocumentViewModel> ConfirmedSet(DockFactory factory)
    {
        var field = typeof(DockFactory).GetField("_closeConfirmed", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (IReadOnlyCollection<FileDocumentViewModel>)field!.GetValue(factory)!;
    }

    [Fact]
    public async Task Confirmed_close_after_save_drops_the_document_from_the_guard_set()
    {
        var factory = NewFactory();
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        // The shell's prompt, Save branch: the save succeeds (dirty flag cleared) and the close proceeds.
        factory.DocumentCloseGuard = doc =>
        {
            doc.SetText(doc.DocumentText, reparse: false);
            return Task.FromResult(true);
        };

        var document = new FileDocumentViewModel("/invented/cave/a.th", "survey a\nendsurvey\n", new MeasurementsViewModel());
        factory.OpenDocument(document);
        document.DocumentText = "survey a\n# edited\nendsurvey\n";
        Assert.True(document.IsDirty);

        factory.CloseDockable(document);
        // The guard completes synchronously, so the re-issued close has already run.
        await Task.Yield();

        Assert.False(document.IsDirty);
        Assert.True(ConfirmedSet(factory).Count == 0,
            $"the close-confirmed set still holds {ConfirmedSet(factory).Count} document(s) after a confirmed close through the Save branch");
    }
}
