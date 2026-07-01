// STRUCT-01 Phase 4 — loopback asset host for the structural-geology 3D plot (three.js in a
// NativeWebView). Independent of the VIS-01 CaveView host (own asset root, own random port, own
// lifecycle) so the two viewers can never interfere. Serves only static avares assets
// (viewer.html + lib/three.min.js + structural.js); the plane/cave-line data is pushed into the page
// via InvokeScript, so there is no model-staging endpoint here.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;

namespace TherionProc.Services;

public interface IStructuralPlotAssetHost : IDisposable
{
    bool IsAvailable { get; }
    bool IsListening { get; }
    bool TryStart();
    string? ViewerUrl { get; }
}

public sealed class StructuralPlotAssetHost : IStructuralPlotAssetHost
{
    private const string AssetRoot = "avares://TherionProc/Assets/structural/";
    private const string ViewerAsset = AssetRoot + "viewer.html";
    private const string ThreeAsset = AssetRoot + "lib/three.min.js";

    private readonly ILogger? _logger;
    private readonly Func<Uri, bool> _assetExists;
    private readonly Func<Uri, Stream> _assetOpen;

    private HttpListener? _listener;
    private int _port;
    private volatile bool _disposed;

    public StructuralPlotAssetHost(ILogger<StructuralPlotAssetHost>? logger = null)
        : this(logger, assetExists: null, assetOpen: null) { }

    internal StructuralPlotAssetHost(ILogger? logger, Func<Uri, bool>? assetExists, Func<Uri, Stream>? assetOpen)
    {
        _logger = logger;
        _assetExists = assetExists ?? (uri => AssetLoader.Exists(uri));
        _assetOpen = assetOpen ?? (uri => AssetLoader.Open(uri));
    }

    public bool IsAvailable
    {
        get
        {
            try { return _assetExists(new Uri(ViewerAsset)) && _assetExists(new Uri(ThreeAsset)); }
            catch { return false; }
        }
    }

    public bool IsListening => _listener?.IsListening ?? false;

    public string? ViewerUrl => IsListening ? $"http://127.0.0.1:{_port}/viewer.html" : null;

    public bool TryStart()
    {
        if (_disposed) return false;
        if (IsListening) return true;
        if (!IsAvailable) { _logger?.LogWarning("Structural plot assets missing; not starting asset host."); return false; }
        try
        {
            _port = FindFreeLoopbackPort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            listener.Start();
            _listener = listener;
            _ = Task.Run(AcceptLoopAsync);
            _logger?.LogInformation("Structural plot asset host listening on {Url}.", ViewerUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not start the structural plot asset host.");
            _listener = null;
            return false;
        }
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener;
        if (listener is null) return;
        while (!_disposed && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            try { Serve(ctx); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Structural plot request failed."); }
        }
    }

    private void Serve(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            res.Headers["Cache-Control"] = "no-store";
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
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

    /// <summary>Maps an HTTP request path to a vendored avares: asset URI, or null if out of bounds.</summary>
    internal static string? MapRequestToAsset(string absolutePath)
    {
        var rel = (absolutePath ?? "/").TrimStart('/');
        if (rel.Length == 0) rel = "viewer.html";
        if (rel.Contains("..", StringComparison.Ordinal) || rel.Contains(':')) return null;
        return AssetRoot + rel;
    }

    internal static string MimeTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };

    internal static int FindFreeLoopbackPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }
}
