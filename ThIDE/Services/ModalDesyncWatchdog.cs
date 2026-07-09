// Recovery + diagnostics for a stuck "modal" main window (Windows only).
//
// Avalonia keeps a window's Win32 enabled bit in sync with its dialog children: Window.ShowDialog
// calls AddChild -> UpdateEnabled -> EnableWindow(owner, FALSE), and closing the dialog re-enables it.
// Native shell dialogs (the file/folder pickers) disable and re-enable the owner themselves, on their
// own thread. When those two schemes get out of step the owner is left disabled: the window still
// repaints and pumps messages, so Windows does not mark it "Not Responding", but the title bar and
// every control ignore input — the app looks permanently frozen.
//
// This has been reported after the scaffold flow (file picker -> modal dialog -> folder picker) and
// could not be reproduced with synthetic input, so we cannot fix the cause yet. Instead we detect the
// desync — the window is disabled while it owns no visible window that could be modal — log the state
// for whoever chases it next, and re-enable the window so the session isn't lost.
//
// The decision logic lives in ModalDesyncDetector so it can be tested without a UI.

using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ThIDE.Services;

/// <summary>Pure decision core: when does "disabled with no owned window" mean the window is stuck?</summary>
public static class ModalDesyncDetector
{
    /// <summary>
    /// Consecutive observations before we act. ShowDialog disables the owner a moment before the dialog
    /// window exists, so a single sample can catch a legitimate modal mid-open; several seconds of
    /// "disabled but nothing owns me" cannot.
    /// </summary>
    public const int StrikesBeforeRecovery = 3;

    /// <summary>Strike count after one observation. Any sign of a real modal resets it to zero.</summary>
    public static int NextStrikes(bool windowEnabled, bool ownsVisibleWindow, int strikes) =>
        windowEnabled || ownsVisibleWindow ? 0 : strikes + 1;

    public static bool ShouldRecover(int strikes) => strikes >= StrikesBeforeRecovery;
}

public sealed class ModalDesyncWatchdog : IDisposable
{
    private readonly Window _window;
    private readonly ILogService? _log;
    private readonly INotificationService? _notifications;
    private DispatcherTimer? _timer;
    private int _strikes;

    public ModalDesyncWatchdog(Window window, ILogService? log, INotificationService? notifications)
    {
        _window = window;
        _log = log;
        _notifications = notifications;
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) return;   // EnableWindow has no counterpart elsewhere
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        if (_window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;

        var enabled = IsWindowEnabled(hwnd);
        _strikes = ModalDesyncDetector.NextStrikes(enabled, OwnsVisibleWindow(hwnd), _strikes);
        if (!ModalDesyncDetector.ShouldRecover(_strikes)) return;

        _strikes = 0;
        _log?.Warning($"Main window was disabled with no owned modal window for " +
                      $"{ModalDesyncDetector.StrikesBeforeRecovery}s — re-enabling it. " +
                      $"Avalonia dialog children: {DescribeDialogChildren()}.");
        EnableWindow(hwnd, true);
        _notifications?.Warning(Resources.Tr.Get("Notif_UnstuckTitle"), Resources.Tr.Get("Notif_UnstuckBody"));
    }

    // What Avalonia believes is modal right now — the half of the state we can name.
    private string DescribeDialogChildren()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return "unknown";
        var others = string.Empty;
        foreach (var w in desktop.Windows)
            if (!ReferenceEquals(w, _window))
                others += (others.Length > 0 ? ", " : string.Empty) + $"{w.GetType().Name}(visible={w.IsVisible})";
        return others.Length == 0 ? "none" : others;
    }

    // True when any visible top-level window is owned by hwnd — an Avalonia dialog or a shell picker.
    // Both are legitimate reasons for the owner to be disabled.
    private static bool OwnsVisibleWindow(IntPtr owner)
    {
        var found = false;
        EnumWindows((h, _) =>
        {
            if (GetWindow(h, GW_OWNER) == owner && IsWindowVisible(h)) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }

    private const uint GW_OWNER = 4;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr hWnd, bool enable);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
}
