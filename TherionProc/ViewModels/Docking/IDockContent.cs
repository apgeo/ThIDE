namespace TherionProc.ViewModels.Docking;

/// <summary>
/// Marker for the leaf Dock dockables whose content is rendered by the app's
/// <see cref="ViewLocator"/> (documents + tool panes). Container docks
/// (RootDock/DocumentDock/ToolDock) are rendered by the Dock theme instead, so
/// they deliberately do not implement this.
/// </summary>
public interface IDockContent
{
}
