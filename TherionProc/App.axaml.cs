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
    /// <summary>
    /// UX-09: file paths passed on the command line (OS "Open with TherionProc" / double-click of an
    /// associated .th/.thconfig). Captured at startup and opened by <see cref="MainWindow"/> once it
    /// is shown. Empty when launched without file arguments.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<string> StartupFileArgs { get; private set; } =
        System.Array.Empty<string>();

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

        // PERF-06: record that we're running (and detect a leftover sentinel from a crashed run,
        // which puts the app in safe mode) before any layout/session restore happens.
        services.GetRequiredService<Services.ICrashRecoveryService>().MarkRunning();

        // Apply the persisted theme (mode + custom syntax colors) before the window shows (#2).
        services.GetRequiredService<Services.IThemeService>().Apply();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // UX-09: stash any file paths passed on the command line so MainWindow can open them
            // after it has shown (covers OS file association + "Open with TherionProc").
            StartupFileArgs = ParseFileArgs(desktop.Args);

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

    // UX-09: keep only arguments that look like real, openable Therion file paths (skip option
    // flags and non-existent paths). Extensions mirror MainWindow.OpenableExtensions.
    private static System.Collections.Generic.IReadOnlyList<string> ParseFileArgs(string[]? args)
    {
        if (args is null || args.Length == 0) return System.Array.Empty<string>();
        var result = new System.Collections.Generic.List<string>();
        foreach (var a in args)
        {
            if (string.IsNullOrWhiteSpace(a) || a.StartsWith('-') || a.StartsWith('/')) continue;
            try { if (System.IO.File.Exists(a)) result.Add(System.IO.Path.GetFullPath(a)); }
            catch { /* malformed path arg — ignore */ }
        }
        return result;
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