// Hosts the real Windows Explorer context menu for a file/folder (#5b follow-up:
// "full shell context menu where supported"). Builds the shell IContextMenu for the
// item, pops it up at the cursor over a message-only host window (so owner-drawn
// submenus like "Open With" / "Send To" populate via IContextMenu2/3), and invokes
// the chosen verb. Entirely best-effort and Windows-only; any failure is swallowed.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TherionProc.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsShellContextMenu
{
    /// <summary>Shows the shell menu at the current cursor position.</summary>
    public static void Show(IntPtr ownerHwnd, string path)
    {
        if (!GetCursorPos(out var pt)) { pt.x = 0; pt.y = 0; }
        Show(ownerHwnd, path, pt.x, pt.y);
    }

    public static void Show(IntPtr ownerHwnd, string path, int screenX, int screenY)
    {
        IntPtr fullPidl = IntPtr.Zero, parentPtr = IntPtr.Zero, ctxPtr = IntPtr.Zero, hmenu = IntPtr.Zero;
        IShellFolder? parent = null;
        IContextMenu? ctx = null;
        ShellMenuHost? host = null;
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out fullPidl, 0, out _) != 0 || fullPidl == IntPtr.Zero)
                return;

            var iidShellFolder = typeof(IShellFolder).GUID;
            if (SHBindToParent(fullPidl, ref iidShellFolder, out parentPtr, out var childPidl) != 0 ||
                parentPtr == IntPtr.Zero)
                return;

            parent = (IShellFolder)Marshal.GetObjectForIUnknown(parentPtr);
            var iidContextMenu = typeof(IContextMenu).GUID;
            var apidl = new[] { childPidl };
            if (parent.GetUIObjectOf(ownerHwnd, 1, apidl, ref iidContextMenu, IntPtr.Zero, out ctxPtr) != 0 ||
                ctxPtr == IntPtr.Zero)
                return;

            ctx = (IContextMenu)Marshal.GetObjectForIUnknown(ctxPtr);
            hmenu = CreatePopupMenu();
            if (hmenu == IntPtr.Zero) return;

            const uint CMF_EXPLORE = 0x00000004;
            if (ctx.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, CMF_EXPLORE) < 0) return;

            host = new ShellMenuHost(ctx);
            uint cmd = TrackPopupMenuEx(hmenu,
                TPM_RETURNCMD | TPM_RIGHTBUTTON, screenX, screenY, host.Handle, IntPtr.Zero);

            if (cmd != 0) Invoke(ctx, ownerHwnd, cmd, screenX, screenY);
        }
        catch { /* best-effort: no shell menu */ }
        finally
        {
            host?.Dispose();
            if (hmenu != IntPtr.Zero) DestroyMenu(hmenu);
            if (ctx is not null) Marshal.ReleaseComObject(ctx);
            if (parent is not null) Marshal.ReleaseComObject(parent);
            if (ctxPtr != IntPtr.Zero) Marshal.Release(ctxPtr);
            if (parentPtr != IntPtr.Zero) Marshal.Release(parentPtr);
            if (fullPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(fullPidl);
        }
    }

    private static void Invoke(IContextMenu ctx, IntPtr hwnd, uint cmd, int x, int y)
    {
        const int CMIC_MASK_UNICODE = 0x00004000;
        const int CMIC_MASK_PTINVOKE = 0x20000000;
        const int SW_SHOWNORMAL = 1;

        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
            hwnd = hwnd,
            lpVerb = (IntPtr)(cmd - IdCmdFirst),
            lpVerbW = (IntPtr)(cmd - IdCmdFirst),
            nShow = SW_SHOWNORMAL,
            ptInvoke = new POINT { x = x, y = y },
        };
        ctx.InvokeCommand(ref info);
    }

    private const uint IdCmdFirst = 1;
    private const uint IdCmdLast = 0x7FFF;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    // ---- message-only host so owner-drawn shell submenus render correctly ----

    private sealed class ShellMenuHost : IDisposable
    {
        private readonly IContextMenu2? _ctx2;
        private readonly IContextMenu3? _ctx3;
        private readonly WndProc _proc;       // kept alive for the window's lifetime
        private readonly ushort _atom;
        private readonly string _className;
        public IntPtr Handle { get; }

        public ShellMenuHost(IContextMenu ctx)
        {
            _ctx2 = ctx as IContextMenu2;
            _ctx3 = ctx as IContextMenu3;
            _proc = WindowProc;
            _className = "TherionShellMenuHost_" + Guid.NewGuid().ToString("N");

            var wc = new WNDCLASS { lpfnWndProc = _proc, lpszClassName = _className };
            _atom = RegisterClass(ref wc);
            Handle = CreateWindowEx(0, _className, string.Empty, 0, 0, 0, 0, 0,
                HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint WM_INITMENUPOPUP = 0x0117;
            const uint WM_DRAWITEM = 0x002B;
            const uint WM_MEASUREITEM = 0x002C;
            const uint WM_MENUCHAR = 0x0120;
            const uint WM_MENUSELECT = 0x011F;

            switch (msg)
            {
                case WM_INITMENUPOPUP:
                case WM_DRAWITEM:
                case WM_MEASUREITEM:
                case WM_MENUCHAR:
                case WM_MENUSELECT:
                    if (_ctx3 is not null && _ctx3.HandleMenuMsg2(msg, wParam, lParam, out var res) == 0)
                        return res;
                    if (_ctx2 is not null && _ctx2.HandleMenuMsg(msg, wParam, lParam) == 0)
                        return IntPtr.Zero;
                    break;
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyWindow(Handle);
            if (_atom != 0) UnregisterClass(_className, IntPtr.Zero);
        }
    }

    // ---- COM interfaces ----

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName();
        [PreserveSig] int EnumObjects();
        [PreserveSig] int BindToObject();
        [PreserveSig] int BindToStorage();
        [PreserveSig] int CompareIDs();
        [PreserveSig] int CreateViewObject();
        [PreserveSig] int GetAttributesOf();
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [In] IntPtr[] apidl,
            ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf();
        [PreserveSig] int SetNameOf();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214f4-0000-0000-c000-000000000046")]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
        public POINT ptInvoke;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    // ---- Win32 ----

    [DllImport("shell32.dll")]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(
        IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int w, int h, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
}
