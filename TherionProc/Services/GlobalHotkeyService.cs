// System-wide "build" hotkey (#3). A global hotkey fires even when the app isn't focused.
//
// Cross-platform abstraction: IGlobalHotkeyService is implemented natively only on Windows
// (Win32 RegisterHotKey on a dedicated message-loop thread). Other platforms get a no-op
// implementation so the rest of the app is unaffected — a portable global-hotkey API does
// not exist in the BCL, so this is intentionally isolated behind the interface + factory.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TherionProc.Services;

public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>Raised (on a background thread) when the global build hotkey is pressed.</summary>
    event EventHandler? BuildHotkeyPressed;
    /// <summary>Registers the hotkey and starts listening. Safe to call once.</summary>
    void Start();
}

/// <summary>No-op implementation for platforms without a supported global-hotkey API.</summary>
public sealed class NullGlobalHotkeyService : IGlobalHotkeyService
{
#pragma warning disable CS0067
    public event EventHandler? BuildHotkeyPressed;
#pragma warning restore CS0067
    public void Start() { }
    public void Dispose() { }
}

public static class GlobalHotkeyServiceFactory
{
    public static IGlobalHotkeyService Create() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsGlobalHotkeyService()
            : new NullGlobalHotkeyService();
}

/// <summary>Windows global hotkey (Ctrl+Alt+B → build) via RegisterHotKey on a worker thread.</summary>
public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint VK_B = 0x42;
    private const int HotkeyId = 0xB01D;

    private Thread? _thread;
    private uint _threadId;
    private volatile bool _disposed;

    public event EventHandler? BuildHotkeyPressed;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "GlobalHotkey" };
        _thread.Start();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        // hWnd = NULL routes WM_HOTKEY to this thread's message queue.
        if (!RegisterHotKey(IntPtr.Zero, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_B))
            return; // another app owns the combo — silently give up

        try
        {
            while (!_disposed && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY && (int)msg.wParam == HotkeyId)
                    BuildHotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally { UnregisterHotKey(IntPtr.Zero, HotkeyId); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
