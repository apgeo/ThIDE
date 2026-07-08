// Thin Dock Tool wrappers around the existing content view-models. Each tool is
// a dockable/floatable pane; its View binds to the wrapped VM. The content VMs
// are unchanged singletons supplied by DI.
//
// Content properties are [JsonIgnore]'d so dock-layout serialization (#10) only
// captures the structural dock fields (Id/Title/Proportion/...); on load the
// DockFactory swaps the deserialized skeletons back to these DI singletons by Id.
//
// Titles are localized from Strings.resx and refreshed live when the UI language
// changes (#2). The dock Id is kept stable (English literal) so layout persistence
// and the DockFactory's by-Id swap keep working across languages.

using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels.Docking;

/// <summary>Base for tool dockables: sets common dock capabilities + the localized title.</summary>
public abstract class ToolViewModelBase : Tool, IDockContent
{
    private readonly string _titleKey;

    protected ToolViewModelBase(string id, string titleKey, ILanguageService? lang = null)
    {
        Id = id;
        _titleKey = titleKey;
        CanFloat = true;
        CanClose = false; // tools hide/pin rather than close-to-gone in the VS model
        CanPin = true;
        UpdateTitle();
        if (lang is not null) lang.LanguageChanged += (_, _) => UpdateTitle();
    }

    private void UpdateTitle() => Title = Tr.Get(_titleKey);

    // ---- shared panel window-controls (full-screen / float-to-other-monitor / move-to-centre) ----
    // Raised by the reusable PanelWindowControls buttons; the shell (MainWindowViewModel) handles them
    // via the DockFactory so they work whether the panel is docked or already floating. Living on the
    // base means every tool gets them for free and a new control button is added in exactly one place.
    public event System.EventHandler? FullScreenRequested;
    public event System.EventHandler? FloatOtherScreenRequested;
    public event System.EventHandler? MoveToCenterRequested;

    public void RequestFullScreen() => FullScreenRequested?.Invoke(this, System.EventArgs.Empty);
    public void RequestFloatOtherScreen() => FloatOtherScreenRequested?.Invoke(this, System.EventArgs.Empty);
    public void RequestMoveToCenter() => MoveToCenterRequested?.Invoke(this, System.EventArgs.Empty);
}

// Each tool keeps a parameterless ctor so the dock-layout deserializer can build a
// skeleton; the DockFactory then swaps the skeleton for the DI singleton by Id. DI
// always picks the richer ctor (more resolvable parameters), so these are serializer-only.

public sealed class WorkspaceExplorerToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public WorkspaceExplorerViewModel Explorer { get; }
    public WorkspaceExplorerToolViewModel() : base("Workspace", "Tool_WorkspaceExplorer") => Explorer = null!;
    public WorkspaceExplorerToolViewModel(WorkspaceExplorerViewModel explorer, ILanguageService? lang = null)
        : base("Workspace", "Tool_WorkspaceExplorer", lang) => Explorer = explorer;
}

/// <summary>The Welcome start page. Lives in the central document well and is closeable/reopenable.</summary>
public sealed class WelcomeToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public WelcomeViewModel Welcome { get; }
    public WelcomeToolViewModel() : base("Welcome", "Tool_Welcome") { Welcome = null!; CanClose = true; }
    public WelcomeToolViewModel(WelcomeViewModel welcome, ILanguageService? lang = null)
        : base("Welcome", "Tool_Welcome", lang) { Welcome = welcome; CanClose = true; }
}

public sealed class ObjectBrowserToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public ObjectBrowserViewModel Browser { get; }
    public ObjectBrowserToolViewModel() : base("ObjectBrowser", "Tool_ObjectBrowser") => Browser = null!;
    public ObjectBrowserToolViewModel(ObjectBrowserViewModel browser, ILanguageService? lang = null)
        : base("ObjectBrowser", "Tool_ObjectBrowser", lang) => Browser = browser;
}

public sealed class DiagnosticsToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public DiagnosticsViewModel Diagnostics { get; }
    public DiagnosticsToolViewModel() : base("Diagnostics", "Tool_Diagnostics") => Diagnostics = null!;
    public DiagnosticsToolViewModel(DiagnosticsViewModel diagnostics, ILanguageService? lang = null)
        : base("Diagnostics", "Tool_Diagnostics", lang) => Diagnostics = diagnostics;
}

public sealed class CompilerOutputToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public BuildViewModel Build { get; }
    public CompilerOutputToolViewModel() : base("CompilerOutput", "Tool_CompilerOutput") => Build = null!;
    public CompilerOutputToolViewModel(BuildViewModel build, ILanguageService? lang = null)
        : base("CompilerOutput", "Tool_CompilerOutput", lang) => Build = build;
}

public sealed class GeneratedFilesToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public BuildViewModel Build { get; }
    public GeneratedFilesToolViewModel() : base("GeneratedFiles", "Tool_GeneratedFiles") => Build = null!;
    public GeneratedFilesToolViewModel(BuildViewModel build, ILanguageService? lang = null)
        : base("GeneratedFiles", "Tool_GeneratedFiles", lang) => Build = build;

    /// <summary>#3: raised to briefly flash the panel after it's surfaced from the status link.</summary>
    public event System.EventHandler? FlashRequested;
    public void Flash() => FlashRequested?.Invoke(this, System.EventArgs.Empty);
}

public sealed class OutlineToolViewModel : ToolViewModelBase
{
    // titleKey "Outline" has no resx entry, so Tr.Get falls back to "Outline".
    [JsonIgnore] public OutlineViewModel Outline { get; }
    public OutlineToolViewModel() : base("Outline", "Outline") => Outline = null!;
    public OutlineToolViewModel(OutlineViewModel outline, ILanguageService? lang = null)
        : base("Outline", "Outline", lang) => Outline = outline;
}

/// <summary>a single "Project" pane with Dashboard / Surveys / Audit tabs.</summary>
public sealed class ProjectToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public ProjectDashboardViewModel Dashboard { get; }
    [JsonIgnore] public SurveyTreeViewModel Surveys { get; }
    [JsonIgnore] public ProjectAuditViewModel Audit { get; }
    /// <summary>analytics tabs.</summary>
    [JsonIgnore] public DataAnalyticsViewModel Analytics { get; }
    /// <summary>exploration leads register.</summary>
    [JsonIgnore] public LeadsViewModel Leads { get; }
    /// <summary>TODO/FIXME/QM aggregator.</summary>
    [JsonIgnore] public TodoScanViewModel Todos { get; }
    /// <summary>project metadata editor.</summary>
    [JsonIgnore] public ProjectMetadataViewModel Metadata { get; }
    /// <summary>background-scan / media manager.</summary>
    [JsonIgnore] public MediaManagerViewModel MediaManager { get; }

    public ProjectToolViewModel() : base("Project", "Tool_Project")
    {
        Dashboard = null!;
        Surveys = null!;
        Audit = null!;
        Analytics = null!;
        Leads = null!;
        Todos = null!;
        Metadata = null!;
        MediaManager = null!;
    }

    public ProjectToolViewModel(ProjectDashboardViewModel dashboard, SurveyTreeViewModel surveys,
        ProjectAuditViewModel audit, DataAnalyticsViewModel analytics, LeadsViewModel leads,
        TodoScanViewModel todos, ProjectMetadataViewModel metadata, MediaManagerViewModel mediaManager,
        ILanguageService? lang = null)
        : base("Project", "Tool_Project", lang)
    {
        Dashboard = dashboard;
        Surveys = surveys;
        Audit = audit;
        Analytics = analytics;
        Leads = leads;
        Todos = todos;
        Metadata = metadata;
        MediaManager = mediaManager;
    }
}

/// <summary>#3: activity / diagnostics log panel (bottom dock, after Generated Files).</summary>
public sealed class LogToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public LogViewModel Log { get; }
    public LogToolViewModel() : base("Log", "Log") => Log = null!;
    public LogToolViewModel(LogViewModel log, ILanguageService? lang = null)
        : base("Log", "Log", lang) => Log = log;
}

/// <summary>live centreline preview (plan/elevation sketch).</summary>
public sealed class LivePreviewToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public LivePreviewViewModel Preview { get; }
    // Dock Id stays "LivePreview" (stable for layout persistence); the visible title is the
    // localized "Mainline Preview". (VIS-02 feature, renamed Live → Mainline preview.)
    public LivePreviewToolViewModel() : base("LivePreview", "Tool_LivePreview") => Preview = null!;
    public LivePreviewToolViewModel(LivePreviewViewModel preview, ILanguageService? lang = null)
        : base("LivePreview", "Tool_LivePreview", lang) => Preview = preview;
}

/// <summary>in-app map viewer (PNG/SVG/PDF).</summary>
public sealed class MapViewerToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public MapViewerViewModel Map { get; }
    public MapViewerToolViewModel() : base("MapViewer", "Map Viewer") => Map = null!;
    public MapViewerToolViewModel(MapViewerViewModel map, ILanguageService? lang = null)
        : base("MapViewer", "Map Viewer", lang) => Map = map;
}

/// <summary>embedded 3D model viewer (CaveView.js in a NativeWebView).</summary>
public sealed class Model3DViewerToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public Model3DViewerViewModel Viewer { get; }
    public Model3DViewerToolViewModel() : base("Model3DViewer", "3D Viewer") => Viewer = null!;
    public Model3DViewerToolViewModel(Model3DViewerViewModel viewer, ILanguageService? lang = null)
        : base("Model3DViewer", "3D Viewer", lang) => Viewer = viewer;
}

/// <summary>structural-geology module (plane strike/dip calculator). Off by default.</summary>
public sealed class StructuralGeologyToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public StructuralGeologyViewModel Structural { get; }
    public StructuralGeologyToolViewModel() : base("StructuralGeology", "Structural Geology") => Structural = null!;
    public StructuralGeologyToolViewModel(StructuralGeologyViewModel structural, ILanguageService? lang = null)
        : base("StructuralGeology", "Structural Geology", lang) => Structural = structural;
}

/// <summary>The 3D Plot popped out of the Structural Geology wizard into its own dockable/floatable
/// panel (see <see cref="StructuralGeologyViewModel.PopOutPlotCommand"/>). Wraps the SAME content
/// VM as <see cref="StructuralGeologyToolViewModel"/> — no state is duplicated.</summary>
public sealed class StructuralPlotToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public StructuralGeologyViewModel Structural { get; }
    public StructuralPlotToolViewModel() : base("StructuralPlot", "Struct_PlotPanelTitle") => Structural = null!;
    public StructuralPlotToolViewModel(StructuralGeologyViewModel structural, ILanguageService? lang = null)
        : base("StructuralPlot", "Struct_PlotPanelTitle", lang) => Structural = structural;
}

public sealed class XviToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public XviReferencesViewModel Xvi { get; }
    public XviToolViewModel() : base("Xvi", "Tool_Xvi") => Xvi = null!;
    public XviToolViewModel(XviReferencesViewModel xvi, ILanguageService? lang = null)
        : base("Xvi", "Tool_Xvi", lang) => Xvi = xvi;
}

public sealed class SettingsToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public SettingsViewModel Settings { get; }
    [JsonIgnore] public KeyboardShortcutsViewModel Keyboard { get; }
    public SettingsToolViewModel() : base("Settings", "Tool_Settings")
    {
        Settings = null!;
        Keyboard = null!;
    }
    public SettingsToolViewModel(SettingsViewModel settings, KeyboardShortcutsViewModel keyboard, ILanguageService? lang = null)
        : base("Settings", "Tool_Settings", lang)
    {
        Settings = settings;
        Keyboard = keyboard;
    }
}
