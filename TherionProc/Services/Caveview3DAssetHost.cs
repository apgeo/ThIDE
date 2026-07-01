// asset host for the embedded 3D viewer (CaveView.js in a NativeWebView).
//
// CaveView fetch()es its model from `surveyDirectory` over a *real* HTTP origin, so a
// file:// page is CORS-blocked (plan risk R1). This service serves the vendored CaveView.js
// dist + the host viewer.html (from avares: resources) and the currently-staged model file
// (from a temp dir) over a loopback HttpListener bound to 127.0.0.1 on a random free port.
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

namespace TherionProc.Services;

public interface ICaveview3DAssetHost : IDisposable
{
    /// <summary>True when the vendored CaveView.js bundle + host page are present as resources.</summary>
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
    // Root of the vendored dist + host page (covered by the Assets/** AvaloniaResource glob).
    private const string AssetRoot = "avares://TherionProc/Assets/caveview/";
    private const string BundleAsset = AssetRoot + "CaveView/js/CaveView2.js";
    private const string ViewerAsset = AssetRoot + "viewer.html";

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
        _modelDir = modelDir ?? Path.Combine(Path.GetTempPath(), "TherionProc", "viewer3d",
            Guid.NewGuid().ToString("N"));
    }

    public bool IsAvailable
    {
        get
        {
            try { return _assetExists(new Uri(BundleAsset)) && _assetExists(new Uri(ViewerAsset)); }
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

            // Everything else: a vendored avares resource (viewer.html + CaveView/**).
            if (MapRequestToAsset(path) is { } assetUri && _assetExists(new Uri(assetUri)))
            {
                res.ContentType = MimeTypeFor(assetUri);
                using var src = _assetOpen(new Uri(assetUri));
                src.CopyTo(res.OutputStream);
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

    /// <summary>Maps an HTTP request path to a vendored avares: asset URI, or null if out of bounds.</summary>
    internal static string? MapRequestToAsset(string absolutePath)
    {
        var rel = (absolutePath ?? "/").TrimStart('/');
        if (rel.Length == 0) rel = "viewer.html";
        // Reject traversal / absolute escapes; only serve from under the caveview asset root.
        if (rel.Contains("..", StringComparison.Ordinal) || rel.Contains(':')) return null;
        return AssetRoot + rel;
    }

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
