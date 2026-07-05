// Viewer-aware "open PDF at page N" (#2). The plain "file://…#page=N" URL fragment is only
// honoured by browser-based viewers (Edge/Chrome/PDF.js/Acrobat-in-browser); standalone
// viewers such as SumatraPDF ignore it. So we detect the OS-default PDF application and use
// its native page-open syntax, falling back to the URL fragment when the viewer is unknown
// or cannot be detected.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Therion.Build;

namespace ThIDE.Services;

public interface IPdfPageOpener
{
    /// <summary>Opens <paramref name="pdfPath"/> at the given 1-based page in the default viewer.</summary>
    bool OpenAt(string pdfPath, int page);
}

public sealed class PdfPageOpener : IPdfPageOpener
{
    private readonly IShellOpener _shell;

    public PdfPageOpener(IShellOpener shell) => _shell = shell;

    public bool OpenAt(string pdfPath, int page)
    {
        if (string.IsNullOrEmpty(pdfPath)) return false;
        page = Math.Max(1, page);

        // Try the detected viewer's native syntax first; on any failure (or an unrecognised
        // viewer) fall through to the browser-style URL fragment.
        try
        {
            if (OperatingSystem.IsWindows() && TryOpenWindows(pdfPath, page)) return true;
            if (OperatingSystem.IsLinux() && TryOpenLinux(pdfPath, page)) return true;
        }
        catch { /* fall back to the URL fragment below */ }

        return OpenViaUrlFragment(pdfPath, page);
    }

    /// <summary>Browser-style fallback: the OS default handler opens the file at #page=N.</summary>
    private bool OpenViaUrlFragment(string pdfPath, int page)
    {
        try { return _shell.Open(new Uri(Path.GetFullPath(pdfPath)).AbsoluteUri + "#page=" + page); }
        catch { return false; }
    }

    // ---- Windows: detect the default .pdf handler and adapt the page syntax --------

    private enum PdfFamily { Unknown, Sumatra, AcrobatStyle, Browser }

    [SupportedOSPlatform("windows")]
    private bool TryOpenWindows(string pdfPath, int page)
    {
        var exe = QueryDefaultPdfExecutable();
        if (string.IsNullOrEmpty(exe)) return false;

        var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        var pdf = Quote(Path.GetFullPath(pdfPath));

        switch (FamilyOf(name))
        {
            case PdfFamily.Sumatra:
                // SumatraPDF: -page N; -reuse-instance reuses the existing window.
                Launch(exe, $"-reuse-instance -page {page} {pdf}");
                return true;

            case PdfFamily.AcrobatStyle:
                // Acrobat / Reader / Foxit / PDF-XChange / Nitro share the /A open-action syntax.
                Launch(exe, $"/A \"page={page}\" {pdf}");
                return true;

            case PdfFamily.Browser:
                // A browser is the default: hand it the file URL with the page fragment.
                Launch(exe, Quote(new Uri(Path.GetFullPath(pdfPath)).AbsoluteUri + "#page=" + page));
                return true;

            default:
                return false; // unknown viewer → caller falls back to the URL fragment
        }
    }

    private static PdfFamily FamilyOf(string exeName) => exeName switch
    {
        "sumatrapdf" => PdfFamily.Sumatra,
        "acrobat" or "acrord32" => PdfFamily.AcrobatStyle,
        "foxitpdfreader" or "foxitreader" or "foxit reader" => PdfFamily.AcrobatStyle,
        "pdfxedit" or "pdfxcview" => PdfFamily.AcrobatStyle,
        "nitpropdf" or "nitro_pro" => PdfFamily.AcrobatStyle,
        "msedge" or "chrome" or "firefox" or "brave" or "opera"
            or "vivaldi" or "iexplore" or "launcher" => PdfFamily.Browser,
        _ => PdfFamily.Unknown,
    };

    /// <summary>Resolves the executable registered as the default opener for ".pdf" (Win32 AssocQueryString).</summary>
    [SupportedOSPlatform("windows")]
    private static string? QueryDefaultPdfExecutable()
    {
        const int ASSOCF_NONE = 0;
        const int ASSOCSTR_EXECUTABLE = 2;
        var sb = new StringBuilder(1024);
        uint cch = (uint)sb.Capacity;
        if (AssocQueryString(ASSOCF_NONE, ASSOCSTR_EXECUTABLE, ".pdf", "open", sb, ref cch) != 0)
            return null;

        var path = sb.ToString();
        if (string.IsNullOrWhiteSpace(path)) return null;
        // "No default set" stubs — treat as undetected so we use the URL fallback.
        var leaf = Path.GetFileName(path).ToLowerInvariant();
        if (leaf is "openwith.exe" or "applicationframehost.exe") return null;
        return path;
    }

    private static void Launch(string exe, string arguments) =>
        Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false });

    // ---- Linux: a couple of common viewers expose page flags -----------------------

    private bool TryOpenLinux(string pdfPath, int page)
    {
        // Only used when one of these is the default; xdg-mime tells us which.
        var app = DefaultLinuxPdfApp();
        if (app is null) return false;
        var pdf = Quote(Path.GetFullPath(pdfPath));
        if (app.Contains("okular")) { Launch("okular", $"-p {page} {pdf}"); return true; }
        if (app.Contains("evince")) { Launch("evince", $"--page-index={page} {pdf}"); return true; }
        if (app.Contains("xpdf")) { Launch("xpdf", $"{pdf} {page}"); return true; }
        return false;
    }

    private static string? DefaultLinuxPdfApp()
    {
        try
        {
            var psi = new ProcessStartInfo("xdg-mime", "query default application/pdf")
            { UseShellExecute = false, RedirectStandardOutput = true };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var s = p.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
            p.WaitForExit(2000);
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch { return null; }
    }

    private static string Quote(string s) => "\"" + s + "\"";

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(
        int flags, int str, string pszAssoc, string? pszExtra, StringBuilder? pszOut, ref uint pcchOut);
}
