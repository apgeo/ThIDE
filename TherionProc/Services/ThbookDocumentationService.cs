// Therion book (thbook) documentation lookup (#6). Maps a Therion term/command to its
// page in the bundled thbook PDF (an editable JSON resource, not hardcoded) and opens
// the PDF at that page in the OS default viewer/browser via a "file://…#page=N" URL —
// honoured by Edge/Chrome/PDF.js/Acrobat. The PDF ships as an embedded avares asset and
// is extracted to app-data once so there is a real file path to open.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Platform;
using Therion.Build;

namespace TherionProc.Services;

public interface IThbookDocumentationService
{
    /// <summary>True once at least one term→page mapping has loaded.</summary>
    bool IsAvailable { get; }
    /// <summary>Resolves a term (command keyword or reference kind) to a 1-based PDF page.</summary>
    bool TryGetPage(string term, out int page);
    /// <summary>Opens the bundled thbook PDF at <paramref name="page"/> in the default viewer.</summary>
    bool OpenAtPage(int page);
    /// <summary>Opens the thbook at the page mapped to <paramref name="term"/> (false if none).</summary>
    bool Open(string term);
}

public sealed class ThbookDocumentationService : IThbookDocumentationService
{
    private const string PdfAsset = "avares://TherionProc/Assets/thbook-v6.4.0.pdf";
    private const string PagesAsset = "avares://TherionProc/Assets/thbook-pages.json";

    private readonly IPdfPageOpener _pdfOpener;
    private readonly Dictionary<string, int> _pages = new(StringComparer.OrdinalIgnoreCase);
    private string _pdfFileName = "thbook-v6.4.0.pdf";
    private string? _pdfPath;     // lazily-extracted real file path
    private bool _extractFailed;

    public ThbookDocumentationService(IPdfPageOpener pdfOpener)
    {
        _pdfOpener = pdfOpener;
        LoadPageMap();
    }

    public bool IsAvailable => _pages.Count > 0;

    public bool TryGetPage(string term, out int page)
    {
        page = 0;
        return !string.IsNullOrWhiteSpace(term)
            && _pages.TryGetValue(term.Trim(), out page) && page > 0;
    }

    public bool Open(string term) => TryGetPage(term, out var page) && OpenAtPage(page);

    public bool OpenAtPage(int page)
    {
        var pdf = EnsurePdf();
        if (pdf is null) return false;
        // Adapts the page syntax to the detected default viewer (Sumatra/Acrobat/Foxit/…),
        // falling back to the "file://…#page=N" URL fragment for browser-based viewers (#2).
        return _pdfOpener.OpenAt(pdf, Math.Max(1, page));
    }

    private void LoadPageMap()
    {
        // Prefer the user-editable override in app data; fall back to the embedded default.
        // On first run, write the default to app data so it is easy for the user to edit.
        var overridePath = AppDataPath("thbook-pages.json");
        var json = TryRead(overridePath) ?? TryReadAsset(PagesAsset);
        if (json is not null && !File.Exists(overridePath)) TryWrite(overridePath, json);
        if (json is null) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pdf", out var pf) && pf.ValueKind == JsonValueKind.String)
                _pdfFileName = pf.GetString() ?? _pdfFileName;
            if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Object)
                foreach (var p in pages.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n))
                        _pages[p.Name] = n;
        }
        catch { /* malformed map → leave whatever loaded */ }
    }

    // Extracts the embedded PDF to app data once and returns its on-disk path.
    private string? EnsurePdf()
    {
        if (_pdfPath is not null) return _pdfPath;
        if (_extractFailed) return null;
        try
        {
            var target = AppDataPath(_pdfFileName);
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

    private static string? TryReadAsset(string uri)
    {
        try
        {
            using var s = AssetLoader.Open(new Uri(uri));
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch { return null; }
    }

    private static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    private static void TryWrite(string path, string content)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); }
        catch { /* best-effort */ }
    }

    // Cross-platform app-data path (mirrors AppSettingsService): %AppData% on Windows,
    // XDG/.config fallback elsewhere.
    private static string AppDataPath(string file)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "TherionProc", file);
    }
}
