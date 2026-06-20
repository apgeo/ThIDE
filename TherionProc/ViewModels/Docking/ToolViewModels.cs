// Thin Dock Tool wrappers around the existing content view-models. Each tool is
// a dockable/floatable pane; its View binds to the wrapped VM. The content VMs
// are unchanged singletons supplied by DI.

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

public sealed class WorkspaceExplorerToolViewModel : ToolViewModelBase
{
    public WorkspaceExplorerViewModel Explorer { get; }
    public WorkspaceExplorerToolViewModel(WorkspaceExplorerViewModel explorer)
        : base("Workspace", "Workspace") => Explorer = explorer;
}

public sealed class ObjectBrowserToolViewModel : ToolViewModelBase
{
    public ObjectBrowserViewModel Browser { get; }
    public ObjectBrowserToolViewModel(ObjectBrowserViewModel browser)
        : base("ObjectBrowser", "Object Browser") => Browser = browser;
}

public sealed class DiagnosticsToolViewModel : ToolViewModelBase
{
    public DiagnosticsViewModel Diagnostics { get; }
    public DiagnosticsToolViewModel(DiagnosticsViewModel diagnostics)
        : base("Diagnostics", "Diagnostics") => Diagnostics = diagnostics;
}

public sealed class CompilerOutputToolViewModel : ToolViewModelBase
{
    public BuildViewModel Build { get; }
    public CompilerOutputToolViewModel(BuildViewModel build)
        : base("CompilerOutput", "Compiler Output") => Build = build;
}

public sealed class GeneratedFilesToolViewModel : ToolViewModelBase
{
    public BuildViewModel Build { get; }
    public GeneratedFilesToolViewModel(BuildViewModel build)
        : base("GeneratedFiles", "Generated Files") => Build = build;
}

public sealed class XviToolViewModel : ToolViewModelBase
{
    public XviReferencesViewModel Xvi { get; }
    public XviToolViewModel(XviReferencesViewModel xvi)
        : base("Xvi", "XVI") => Xvi = xvi;
}

public sealed class SettingsToolViewModel : ToolViewModelBase
{
    public SettingsViewModel Settings { get; }
    public KeyboardShortcutsViewModel Keyboard { get; }
    public SettingsToolViewModel(SettingsViewModel settings, KeyboardShortcutsViewModel keyboard)
        : base("Settings", "Settings")
    {
        Settings = settings;
        Keyboard = keyboard;
    }
}
