// Opens the ThIDE User Guide from Help ▸ User Guide. The guide is authored as Markdown in
// docs/user-guide/ and shipped as a single navigable PDF built by build/build-user-guide.ps1
// into Assets/ (globbed AvaloniaResource). This service resolves the best available form:
//   1. the bundled PDF asset  → extracted to app-data once, opened at page 1 (like thbook);
//   2. the Markdown home on disk (developer / not-yet-generated build) → OS default app;
//   3. the online docs on the repository.
// So the menu item always does something sensible whether or not the PDF was generated.

using System;
using System.IO;
using Avalonia.Platform;
using Therion.Build;

namespace ThIDE.Services;

public interface IUserGuideService
{
    /// <summary>
    /// Returns the on-disk path to the bundled user-guide PDF (extracting it from app assets on
    /// first use), or <c>null</c> when no PDF was shipped in this build. Callers load it into the
    /// in-app PDF viewer.
    /// </summary>
    string? TryGetBundledPdfPath();

    /// <summary>
    /// Opens the guide without the in-app viewer: the bundled PDF in the OS default viewer if
    /// present, else the on-disk Markdown source, else the online docs. Returns false if nothing
    /// could be opened.
    /// </summary>
    bool OpenExternally();
}

public sealed class UserGuideService : IUserGuideService
{
    private const string PdfAsset = "avares://ThIDE/Assets/ThIDE-User-Guide.pdf";
    private const string PdfFileName = "ThIDE-User-Guide.pdf";
    private static readonly string OnlineDocsUrl =
        AppEnvironmentInfo.RepositoryUrl + "/tree/main/docs/user-guide";

    private readonly IPdfPageOpener _pdf;
    private readonly IShellOpener _shell;
    private string? _pdfPath;
    private bool _extractFailed;

    public UserGuideService(IPdfPageOpener pdf, IShellOpener shell)
    {
        _pdf = pdf;
        _shell = shell;
    }

    public string? TryGetBundledPdfPath() => EnsurePdf();

    public bool OpenExternally()
    {
        // 1. The shipping path: the bundled PDF in the OS default viewer.
        var pdf = EnsurePdf();
        if (pdf is not null && _pdf.OpenAt(pdf, 1)) return true;

        // 2. Running from source without a generated PDF: open the Markdown home.
        var md = FindLocalMarkdownHome();
        if (md is not null && TryOpen(md)) return true;

        // 3. Last resort: the docs on the web.
        return TryOpen(OnlineDocsUrl);
    }

    // Extracts the embedded PDF to app data once and returns its on-disk path (null if the
    // asset isn't bundled — e.g. the build script hasn't run yet).
    private string? EnsurePdf()
    {
        if (_pdfPath is not null) return _pdfPath;
        if (_extractFailed) return null;
        try
        {
            if (!AssetLoader.Exists(new Uri(PdfAsset))) { _extractFailed = true; return null; }
            var target = AppDataPath(PdfFileName);
            if (!File.Exists(target) || new FileInfo(target).Length == 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var src = AssetLoader.Open(new Uri(PdfAsset));
                using var dst = File.Create(target);
                src.CopyTo(dst);
            }
            _pdfPath = target;
            return target;
        }
        catch { _extractFailed = true; return null; }
    }

    // Walks up from the app's base directory looking for docs/user-guide/README.md, so a
    // developer running from the source tree still gets the guide.
    private static string? FindLocalMarkdownHome()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "docs", "user-guide", "README.md");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { /* best-effort */ }
        return null;
    }

    private bool TryOpen(string target)
    {
        try { return _shell.Open(target); }
        catch { return false; }
    }

    // Cross-platform app-data path (mirrors ThbookDocumentationService): %AppData% on Windows,
    // XDG/.config fallback elsewhere.
    private static string AppDataPath(string file)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", file);
    }
}
