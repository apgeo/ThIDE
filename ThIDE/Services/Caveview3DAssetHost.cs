// asset host for the embedded 3D viewer (CaveView.js in a NativeWebView).
//
// CaveView fetch()es its model from `surveyDirectory` over a *real* HTTP origin, so a
// file:// page is CORS-blocked (plan risk R1). This service serves the host viewer.html (an
// avares: resource), the CaveView.js runtime and the shared station-mark script (embedded in
// Therion.CaveView, the one copy the web-page export uses too) and the currently-staged model
// file (from a temp dir) over a loopback HttpListener bound to 127.0.0.1 on a random free port.
//
// Mirrors the IMapRenderService contract shape: a small clean interface, fully defensive
// (never throws into the UI), IDisposable. The 3D view degrades to a friendly message +
// the loch/aven fallback when IsAvailable is false (engine or assets missing).

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Therion.CaveView;

namespace ThIDE.Services;

public interface ICaveview3DAssetHost : IDisposable
{
    /// <summary>True when the CaveView.js runtime + host page are present as resources.</summary>
    bool IsAvailable { get; }

    /// <summary>True once the loopback server is listening.</summary>
    bool IsListening { get; }

    /// <summary>Starts the loopback server if possible (idempotent). Returns <see cref="IsListening"/>.</summary>
    bool TryStart();

    /// <summary>Absolute URL of the host page (e.g. <c>http://127.0.0.1:49xxx/viewer.html</c>), or null until started.</summary>
    string? ViewerUrl { get; }

    /// <summary>
    /// Stages <paramref name="modelPath"/> (a .lox/.3d) into the served model directory and returns the
    /// bare filename to pass to <c>cvLoadModel()</c> (served under <c>/model/</c>). Null on failure.
    /// </summary>
    string? StageModel(string modelPath);
}

public sealed class Caveview3DAssetHost : ICaveview3DAssetHost
{
    // The host page (covered by the Assets/** AvaloniaResource glob). The runtime it loads is
    // served from Therion.CaveView under /CaveView/, the shared page scripts under /assets/.
    private const string ViewerAsset = "avares://ThIDE/Assets/caveview/viewer.html";

    private readonly ILogger? _logger;
    private readonly string _modelDir;
    private readonly Func<Uri, bool> _assetExists;
    private readonly Func<Uri, Stream> _assetOpen;

    private HttpListener? _listener;
    private int _port;
    private int _modelSeq;
    private string? _currentModelName;
    private volatile bool _disposed;

    public Caveview3DAssetHost(ILogger<Caveview3DAssetHost>? logger = null)
        : this(logger, modelDir: null, assetExists: null, assetOpen: null) { }

    // Test/seam ctor: a custom model dir and asset accessors keep the pure logic testable
    // without the Avalonia asset system or a real HTTP listener.
    internal Caveview3DAssetHost(ILogger? logger, string? modelDir,
        Func<Uri, bool>? assetExists, Func<Uri, Stream>? assetOpen)
    {
        _logger = logger;
        _assetExists = assetExists ?? (uri => AssetLoader.Exists(uri));
        _assetOpen = assetOpen ?? (uri => AssetLoader.Open(uri));
        _modelDir = modelDir ?? Path.Combine(Path.GetTempPath(), "ThIDE", "viewer3d",
            Guid.NewGuid().ToString("N"));
    }

    public bool IsAvailable
    {
        get
        {
            try { return CaveViewRuntime.IsAvailable && _assetExists(new Uri(ViewerAsset)); }
            catch { return false; }
        }
    }

    public bool IsListening => _listener?.IsListening ?? false;

    public string? ViewerUrl => IsListening ? $"http://127.0.0.1:{_port}/viewer.html" : null;

    public bool TryStart()
    {
        if (_disposed) return false;
        if (IsListening) return true;
        if (!IsAvailable) { _logger?.LogWarning("3D viewer assets missing; not starting asset host."); return false; }
        try
        {
            Directory.CreateDirectory(_modelDir);
            _port = FindFreeLoopbackPort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            listener.Start();
            _listener = listener;
            _ = Task.Run(AcceptLoopAsync);
            _logger?.LogInformation("3D viewer asset host listening on {Url}.", ViewerUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not start the 3D viewer asset host.");
            _listener = null;
            return false;
        }
    }

    public string? StageModel(string modelPath)
    {
        if (_disposed) return null;
        try
        {
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath)) return null;
            Directory.CreateDirectory(_modelDir);

            // Stage under a fresh, URL-safe name each call: avoids spaces/unicode in the
            // request path and sidesteps the web engine caching a previous model under the
            // same URL. The extension is preserved so CaveView picks the right loader.
            var ext = Path.GetExtension(modelPath).ToLowerInvariant();
            if (ext is not (".lox" or ".3d")) return null;
            var name = $"m{Interlocked.Increment(ref _modelSeq)}{ext}";
            var dest = Path.Combine(_modelDir, name);
            File.Copy(modelPath, dest, overwrite: true);

            // Drop the previous staged model so the temp dir doesn't accumulate.
            if (_currentModelName is { } prev && !string.Equals(prev, name, StringComparison.Ordinal))
                TryDelete(Path.Combine(_modelDir, prev));
            _currentModelName = name;
            return name;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to stage 3D model {Path}.", modelPath);
            return null;
        }
    }

    // ---- request handling ---------------------------------------------------

    private async Task AcceptLoopAsync()
    {
        var listener = _listener;
        if (listener is null) return;
        while (!_disposed && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; } // listener stopped/disposed
            try { Serve(ctx); }
            catch (Exception ex) { _logger?.LogDebug(ex, "3D asset request failed."); }
        }
    }

    private void Serve(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            res.Headers["Cache-Control"] = "no-store";
            var path = ctx.Request.Url?.AbsolutePath ?? "/";

            // Model file: served from the staged temp dir on disk.
            if (path.StartsWith("/model/", StringComparison.Ordinal))
            {
                var name = Path.GetFileName(path);
                var file = Path.Combine(_modelDir, name);
                if (string.Equals(name, _currentModelName, StringComparison.Ordinal) && File.Exists(file))
                {
                    res.ContentType = "application/octet-stream";
                    using var fs = File.OpenRead(file);
                    res.ContentLength64 = fs.Length;
                    fs.CopyTo(res.OutputStream);
                    return;
                }
                res.StatusCode = 404;
                return;
            }

            // Everything else: the host page, the CaveView runtime or a shared page script.
            if (MapRequestToAsset(path) is { } asset && OpenAsset(asset) is { } src)
            {
                using (src)
                {
                    res.ContentType = MimeTypeFor(asset.Path);
                    src.CopyTo(res.OutputStream);
                }
                return;
            }

            res.StatusCode = 404;
        }
        finally
        {
            try { res.OutputStream.Close(); } catch { }
        }
    }

    // ---- pure helpers (unit-tested) -----------------------------------------

    internal enum AssetKind { Viewer, Runtime, Page }

    /// <summary>A served file: the host page, a runtime file (path under the runtime's home) or a page file.</summary>
    internal readonly record struct AssetRef(AssetKind Kind, string Path);

    /// <summary>Maps an HTTP request path to the file it serves, or null if out of bounds.</summary>
    internal static AssetRef? MapRequestToAsset(string absolutePath)
    {
        var rel = (absolutePath ?? "/").TrimStart('/');
        if (rel.Length == 0) rel = "viewer.html";
        // Reject traversal / absolute escapes.
        if (rel.Contains("..", StringComparison.Ordinal) || rel.Contains(':') || rel.Contains('\\')) return null;
        if (rel == "viewer.html") return new AssetRef(AssetKind.Viewer, rel);
        if (rel.StartsWith("CaveView/", StringComparison.Ordinal)) return new AssetRef(AssetKind.Runtime, rel["CaveView/".Length..]);
        if (rel.StartsWith("assets/", StringComparison.Ordinal)) return new AssetRef(AssetKind.Page, rel);
        return null;
    }

    private Stream? OpenAsset(AssetRef asset) => asset.Kind switch
    {
        AssetKind.Viewer => _assetExists(new Uri(ViewerAsset)) ? _assetOpen(new Uri(ViewerAsset)) : null,
        AssetKind.Runtime => CaveViewRuntime.Open(asset.Path),
        _ => CaveViewRuntime.OpenPageFile(asset.Path),
    };

    /// <summary>Content-type for the small set of file kinds the viewer serves.</summary>
    internal static string MimeTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs"   => "text/javascript; charset=utf-8",
            ".css"            => "text/css; charset=utf-8",
            ".json"           => "application/json; charset=utf-8",
            ".svg"            => "image/svg+xml",
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".wasm"           => "application/wasm",
            ".lox" or ".3d"   => "application/octet-stream",
            _                  => "application/octet-stream",
        };

    /// <summary>Reserves a free TCP port on the loopback interface (then releases it for HttpListener).</summary>
    internal static int FindFreeLoopbackPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        try { if (Directory.Exists(_modelDir)) Directory.Delete(_modelDir, recursive: true); } catch { }
    }
}
