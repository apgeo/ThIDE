using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TherionProc.ViewModels;
using TherionProc.Views;

namespace TherionProc;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

    // disabled for now
    // #if DEBUG
    //     this.AttachDeveloperTools();
    // #endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Swallow a benign Dock.Avalonia bug: its global pointer-moved handler calls
        // PointToScreen on a control that briefly isn't in a visual tree (e.g. while a
        // popup/flyout is up), throwing "Control does not belong to a visual tree". It's
        // harmless to the app, so we mark it handled instead of letting it crash (task 3).
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

        var services = AppServices.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the main window quits the app, even if tool/float/search windows
            // are still open (#9).
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (IsBenignDockHoverError(e.Exception)) e.Handled = true;
    }

    // Narrowly matches the Dock.Avalonia DockControl.MovedHandler PointToScreen failure so
    // we never mask unrelated exceptions.
    private static bool IsBenignDockHoverError(Exception ex)
    {
        if (ex is not ArgumentException) return false;
        var trace = ex.StackTrace ?? string.Empty;
        return (trace.Contains("DockControl", StringComparison.Ordinal) ||
                trace.Contains("PointToScreen", StringComparison.Ordinal))
            && ex.Message.Contains("visual tree", StringComparison.OrdinalIgnoreCase);
    }
}