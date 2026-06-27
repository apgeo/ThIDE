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
using TherionProc.Resources;
using TherionProc.Services;

namespace TherionProc.ViewModels.Docking;

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
}

public sealed class OutlineToolViewModel : ToolViewModelBase
{
    // EDIT-09: titleKey "Outline" has no resx entry, so Tr.Get falls back to "Outline".
    [JsonIgnore] public OutlineViewModel Outline { get; }
    public OutlineToolViewModel() : base("Outline", "Outline") => Outline = null!;
    public OutlineToolViewModel(OutlineViewModel outline, ILanguageService? lang = null)
        : base("Outline", "Outline", lang) => Outline = outline;
}

/// <summary>PROJ-02/03/07: a single "Project" pane with Dashboard / Surveys / Audit tabs.</summary>
public sealed class ProjectToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public ProjectDashboardViewModel Dashboard { get; }
    [JsonIgnore] public SurveyTreeViewModel Surveys { get; }
    [JsonIgnore] public ProjectAuditViewModel Audit { get; }

    public ProjectToolViewModel() : base("Project", "Project")
    {
        Dashboard = null!;
        Surveys = null!;
        Audit = null!;
    }

    public ProjectToolViewModel(ProjectDashboardViewModel dashboard, SurveyTreeViewModel surveys,
        ProjectAuditViewModel audit, ILanguageService? lang = null)
        : base("Project", "Project", lang)
    {
        Dashboard = dashboard;
        Surveys = surveys;
        Audit = audit;
    }
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
