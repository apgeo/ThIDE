// Provides native OS shell icons for the file-explorer tree (#5b follow-up). On
// Windows it pulls the per-extension / folder icon from the shell via SHGetFileInfo
// and converts the HICON into an Avalonia bitmap; everywhere else it returns null so
// the tree falls back to the built-in Material glyphs. Icons are cached (per extension
// for files, one shared folder icon) so the tree stays cheap to build.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TherionProc.Services;

public interface IFileIconProvider
{
    /// <summary>The small shell icon for a path, or null when unavailable (caller uses a glyph).</summary>
    IImage? GetIcon(string path, bool isDirectory);
}

public sealed class FileIconProvider : IFileIconProvider
{
    private readonly ConcurrentDictionary<string, IImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IImage? GetIcon(string path, bool isDirectory)
    {
        if (!OperatingSystem.IsWindows()) return null;

        // Cache key: folders share one icon; most files share their extension's icon, but
        // self-iconned types (.exe/.lnk/.ico) are unique to the file.
        var ext = Path.GetExtension(path);
        bool unique = !isDirectory &&
            (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
             ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
             ext.Equals(".ico", StringComparison.OrdinalIgnoreCase));
        var key = isDirectory ? "<dir>"
            : unique ? path
            : string.IsNullOrEmpty(ext) ? "<noext>" : ext.ToLowerInvariant();

        return _cache.GetOrAdd(key, _ => Load(path, isDirectory, unique));
    }

    private static IImage? Load(string path, bool isDirectory, bool unique)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return WindowsShellIcons.GetSmallIcon(path, isDirectory, useRealFile: unique); }
        catch { return null; }
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsShellIcons
{
    public static IImage? GetSmallIcon(string path, bool isDirectory, bool useRealFile)
    {
        uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        // USEFILEATTRIBUTES avoids touching the disk and gives the per-type/folder icon;
        // for self-iconned files we read the real file so its embedded icon shows.
        if (!useRealFile) flags |= SHGFI_USEFILEATTRIBUTES;

        var info = new SHFILEINFO();
        var res = SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try { return IconToBitmap(info.hIcon); }
        finally { DestroyIcon(info.hIcon); }
    }

    private static IImage? IconToBitmap(IntPtr hIcon)
    {
        int w = GetSystemMetrics(SM_CXSMICON); if (w <= 0) w = 16;
        int h = GetSystemMetrics(SM_CYSMICON); if (h <= 0) h = 16;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,          // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        IntPtr hbmp = CreateDIBSection(memDc, ref header, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero || bits == IntPtr.Zero)
        {
            DeleteDC(memDc); ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        IntPtr old = SelectObject(memDc, hbmp);
        DrawIconEx(memDc, 0, 0, hIcon, w, h, 0, IntPtr.Zero, DI_NORMAL);
        SelectObject(memDc, old);

        try
        {
            int srcStride = w * 4;
            var managed = new byte[srcStride * h];
            Marshal.Copy(bits, managed, 0, managed.Length);

            // DrawIconEx writes straight (un-premultiplied) BGRA into the cleared DIB.
            var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = wb.Lock())
            {
                if (fb.RowBytes == srcStride)
                    Marshal.Copy(managed, 0, fb.Address, managed.Length);
                else
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(managed, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
            }
            return wb;
        }
        finally
        {
            DeleteObject(hbmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---- Win32 ----

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;
    private const uint DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y,
        IntPtr hIcon, int w, int h, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi,
        uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
}
