// A render server for the tests of the render node worker: the protocol's endpoints on 127.0.0.1
// at a free port, jobs handed out from a queue, bundles served from memory, and everything a node
// sent kept for the test to look at. Answers can be made to fail (5xx, 401, 409) on purpose.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Therion.Blender.Tests;

internal sealed class FakeRenderServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <summary>How many ports are tried before the server gives up.</summary>
    public const int PortTries = 10;

    public FakeRenderServer() : this(FreePort) { }

    /// <param name="port">Picks a port to listen on. The port is free when it is picked, but
    /// something else may take it before the listener starts: a fresh port, and a fresh
    /// listener, are then tried.</param>
    internal FakeRenderServer(Func<int> port)
    {
        for (int tries = 1; ; tries++)
        {
            var listener = new HttpListener();
            int candidate = port();
            listener.Prefixes.Add($"http://127.0.0.1:{candidate}/");
            listener.Prefixes.Add($"http://localhost:{candidate}/");
            try
            {
                listener.Start();
                _listener = listener;
                Port = candidate;
                break;
            }
            catch (HttpListenerException) when (tries < PortTries)
            {
                try { listener.Close(); } catch (HttpListenerException) { }
            }
        }
        _loop = Task.Run(LoopAsync);
    }

    public int Port { get; }

    public string Token { get; set; } = "node-token-5f2c9a";

    public Uri Url => new($"http://127.0.0.1:{Port}/api/nodes");

    public ConcurrentQueue<RenderNodeJob> Jobs { get; } = new();

    public ConcurrentDictionary<string, byte[]> Bundles { get; } = new();

    public ConcurrentQueue<RenderNodeClaim> Claims { get; } = new();

    public ConcurrentQueue<RenderNodeHeartbeat> Beats { get; } = new();

    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    public ConcurrentQueue<(string Job, RenderNodeComplete Message)> Completed { get; } = new();

    public ConcurrentQueue<(string Job, RenderNodeFail Message)> Failed { get; } = new();

    /// <summary>Each request: its method and path, and whether it carried the right token.</summary>
    public ConcurrentQueue<(string Request, bool Token)> Requests { get; } = new();

    /// <summary>The answer to a heartbeat, from the beats so far: a status, and the action for 200.</summary>
    public Func<int, (int Status, string Action)> Heartbeat { get; set; } = _ => (200, RenderNodeProtocol.Continue);

    /// <summary>A status to answer a request with in place of its own ("POST claim", "PUT …"); null to answer it.</summary>
    public Func<string, int?> Fault { get; set; } = _ => null;

    /// <summary>A bundle served at <c>/bundles/{name}</c>, and the job that names it.</summary>
    public RenderNodeJob Offer(string jobId, string zipPath, string plan = "full", string? container = null, int lease = 6,
        string? sha256 = null, RenderNodeLimits? limits = null, int attempt = 1, string? host = null)
    {
        byte[] bytes = File.ReadAllBytes(zipPath);
        Bundles[jobId + ".zip"] = bytes;
        var job = new RenderNodeJob
        {
            JobId = jobId, Attempt = attempt, LeaseSeconds = lease, Plan = plan, Container = container, Limits = limits,
            Bundle = new RenderNodeBundle
            {
                Url = host is null ? $"/bundles/{jobId}.zip" : $"http://{host}:{Port}/bundles/{jobId}.zip",
                Size = bytes.Length,
                Sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            },
        };
        Jobs.Enqueue(job);
        return job;
    }

    internal static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening) { return; }
            catch (HttpListenerException) { return; }
            _ = Task.Run(() => Handle(context));
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            string path = request.Url!.AbsolutePath;
            string name = $"{request.HttpMethod} {path}";
            bool token = request.Headers["Authorization"] == "Bearer " + Token;
            Requests.Enqueue((name, token));
            if (Fault(name) is { } fault)
            {
                Drain(request);
                Answer(response, fault);
                return;
            }
            if (path.StartsWith("/bundles/", StringComparison.Ordinal))
            {
                if (!Bundles.TryGetValue(path["/bundles/".Length..], out var bundle)) Answer(response, 404);
                else
                {
                    response.ContentLength64 = bundle.Length;
                    response.OutputStream.Write(bundle);
                    response.Close();
                }
                return;
            }
            if (!token)
            {
                Drain(request);
                Answer(response, 401);
                return;
            }
            const string root = "/api/nodes/";
            if (!path.StartsWith(root, StringComparison.Ordinal))
            {
                Answer(response, 404);
                return;
            }
            var parts = path[root.Length..].Split('/');
            switch (request.HttpMethod, parts)
            {
                case ("POST", ["claim"]):
                    Claims.Enqueue(RenderNodeProtocol.Read<RenderNodeClaim>(Body(request))!);
                    if (Jobs.TryDequeue(out var job)) Answer(response, 200, RenderNodeProtocol.Write(job));
                    else Answer(response, 204);
                    return;
                case ("POST", ["jobs", _, "heartbeat"]):
                    Beats.Enqueue(RenderNodeProtocol.Read<RenderNodeHeartbeat>(Body(request))!);
                    var (status, action) = Heartbeat(Beats.Count);
                    if (status == 200) Answer(response, 200, RenderNodeProtocol.Write(new RenderNodeHeartbeatReply { Action = action }));
                    else Answer(response, status);
                    return;
                case ("PUT", ["jobs", var id, "files", var file]):
                    using (var body = new MemoryStream())
                    {
                        request.InputStream.CopyTo(body);
                        byte[] bytes = body.ToArray();
                        bool matches = request.Headers[RenderNodeProtocol.Sha256Header] == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                                       && request.QueryString["attempt"] is { Length: > 0 };
                        if (matches) Files[id + "/" + Uri.UnescapeDataString(file)] = bytes;
                        Answer(response, matches ? 200 : 400);
                    }
                    return;
                case ("POST", ["jobs", var id, "complete"]):
                    Completed.Enqueue((id, RenderNodeProtocol.Read<RenderNodeComplete>(Body(request))!));
                    Answer(response, 200);
                    return;
                case ("POST", ["jobs", var id, "fail"]):
                    Failed.Enqueue((id, RenderNodeProtocol.Read<RenderNodeFail>(Body(request))!));
                    Answer(response, 200);
                    return;
                default:
                    Answer(response, 404);
                    return;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private static string Body(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Drain(HttpListenerRequest request)
    {
        try { request.InputStream.CopyTo(Stream.Null); } catch (Exception ex) when (ex is IOException or HttpListenerException) { }
    }

    private static void Answer(HttpListenerResponse response, int status, string? json = null)
    {
        response.StatusCode = status;
        if (json is not null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
        }
        response.Close();
    }

    public void Dispose()
    {
        _stop.Cancel();
        // On Linux, closing a listener looks its port up again, and can bind it anew to do so: a
        // port someone else has taken since then makes Close throw, with nothing left to free.
        try { _listener.Stop(); _listener.Close(); } catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException) { }
        try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        _stop.Dispose();
    }
}
