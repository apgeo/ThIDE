// Thin Dock Tool wrappers around the existing content view-models. Each tool is
// a dockable/floatable pane; its View binds to the wrapped VM. The content VMs
// are unchanged singletons supplied by DI.
//
// Content properties are [JsonIgnore]'d so dock-layout serialization (#10) only
// captures the structural dock fields (Id/Title/Proportion/...); on load the
// DockFactory swaps the deserialized skeletons back to these DI singletons by Id.

using System.Text.Json.Serialization;
using Dock.Model.Mvvm.Controls;

namespace TherionProc.ViewModels.Docking;

/// <summary>Base for tool dockables: sets common dock capabilities + the content marker.</summary>
public abstract class ToolViewModelBase : Tool, IDockContent
{
    protected ToolViewModelBase(string id, string title)
    {
        Id = id;
        Title = title;
        CanFloat = true;
        CanClose = false; // tools hide/pin rather than close-to-gone in the VS model
        CanPin = true;
    }
}

// Each tool keeps a parameterless ctor so the dock-layout deserializer can build a
// skeleton; the DockFactory then swaps the skeleton for the DI singleton by Id. DI
// always picks the richer ctor (more resolvable parameters), so these are serializer-only.

public sealed class WorkspaceExplorerToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public WorkspaceExplorerViewModel Explorer { get; }
    public WorkspaceExplorerToolViewModel() : base("Workspace", "Workspace") => Explorer = null!;
    public WorkspaceExplorerToolViewModel(WorkspaceExplorerViewModel explorer)
        : base("Workspace", "Workspace") => Explorer = explorer;
}

public sealed class ObjectBrowserToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public ObjectBrowserViewModel Browser { get; }
    public ObjectBrowserToolViewModel() : base("ObjectBrowser", "Object Browser") => Browser = null!;
    public ObjectBrowserToolViewModel(ObjectBrowserViewModel browser)
        : base("ObjectBrowser", "Object Browser") => Browser = browser;
}

public sealed class DiagnosticsToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public DiagnosticsViewModel Diagnostics { get; }
    public DiagnosticsToolViewModel() : base("Diagnostics", "Diagnostics") => Diagnostics = null!;
    public DiagnosticsToolViewModel(DiagnosticsViewModel diagnostics)
        : base("Diagnostics", "Diagnostics") => Diagnostics = diagnostics;
}

public sealed class CompilerOutputToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public BuildViewModel Build { get; }
    public CompilerOutputToolViewModel() : base("CompilerOutput", "Compiler Output") => Build = null!;
    public CompilerOutputToolViewModel(BuildViewModel build)
        : base("CompilerOutput", "Compiler Output") => Build = build;
}

public sealed class GeneratedFilesToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public BuildViewModel Build { get; }
    public GeneratedFilesToolViewModel() : base("GeneratedFiles", "Generated Files") => Build = null!;
    public GeneratedFilesToolViewModel(BuildViewModel build)
        : base("GeneratedFiles", "Generated Files") => Build = build;
}

public sealed class XviToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public XviReferencesViewModel Xvi { get; }
    public XviToolViewModel() : base("Xvi", "XVI") => Xvi = null!;
    public XviToolViewModel(XviReferencesViewModel xvi)
        : base("Xvi", "XVI") => Xvi = xvi;
}

public sealed class SettingsToolViewModel : ToolViewModelBase
{
    [JsonIgnore] public SettingsViewModel Settings { get; }
    [JsonIgnore] public KeyboardShortcutsViewModel Keyboard { get; }
    public SettingsToolViewModel() : base("Settings", "Settings")
    {
        Settings = null!;
        Keyboard = null!;
    }
    public SettingsToolViewModel(SettingsViewModel settings, KeyboardShortcutsViewModel keyboard)
        : base("Settings", "Settings")
    {
        Settings = settings;
        Keyboard = keyboard;
    }
}
