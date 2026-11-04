// The window a floated panel lives in, owned by the main window so it stays in front of it.
// Dock.Avalonia picks a float's owner from the visual root of the factory's first DockControl; here
// that never resolved to a Window, so every float was shown without an owner (no WM_TRANSIENT_FOR on
// X11) and dropped behind the main window the moment the main window was clicked. The owner is now
// the application's main window, the same one the app's own dialogs use.

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace ThIDE.Docking;

internal sealed class OwnedHostWindow : HostWindow, IHostWindow
{
    void IHostWindow.Present(bool isDialog)
    {
        if (isDialog || IsVisible || MainWindow() is not { } main || ReferenceEquals(main, this))
        {
            Present(isDialog);
            return;
        }
        // What HostWindow.Present does before showing a float, then the show itself with the owner.
        if (Window is { } model)
        {
            model.Factory?.OnWindowOpened(model);
            if (model.ShowInTaskbar is { } inTaskbar) ShowInTaskbar = inTaskbar;
        }
        Show(main);
    }

    private static Window? MainWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } main }
            ? main : null;
}
