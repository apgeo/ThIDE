// The test server itself: it must start every time, or the tests of the render node fail for a
// reason of their own ("Address already in use"). A free port can be taken by something else
// between the moment it is found and the moment the listener starts; the server then tries another.

using System.Net;
using System.Net.Sockets;

namespace Therion.Blender.Tests;

/// <summary>Run alone: hundreds of servers that claim jobs of their own must not meet the servers
/// of the render node's tests (on Windows, http.sys shares one port space among them all).</summary>
[CollectionDefinition(nameof(FakeRenderServerTests), DisableParallelization = true)]
public sealed class FakeRenderServerTestsCollection;

[Collection(nameof(FakeRenderServerTests))]
public sealed class FakeRenderServerTests
{
    [Fact]
    public async Task APortTakenBetweenTheProbeAndTheStart_StartsTheServerOnAnother()
    {
        // The first port it is given is taken before the listener starts on it.
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        int busy = ((IPEndPoint)taken.LocalEndpoint).Port;
        int tries = 0;
        try
        {
            using var server = new FakeRenderServer(() => ++tries == 1 ? busy : FakeRenderServer.FreePort());
            Assert.NotEqual(busy, server.Port);
            Assert.Equal(2, tries);
            using var http = new HttpClient();
            using var answer = await http.PostAsync(new Uri(server.Url, "nodes/claim"), new StringContent("{}"));
            Assert.NotEqual(0, (int)answer.StatusCode);
        }
        finally { taken.Stop(); }
    }

    [Fact]
    public async Task ManyServersAtOnce_EachStarts()
    {
        int failures = 0;
        string? first = null;
        var loops = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < 40; i++)
            {
                FakeRenderServer? server = null;
                try
                {
                    server = new FakeRenderServer();
                    using var http = new HttpClient();
                    using var answer = await http.PostAsync(new Uri(server.Url, "nodes/claim"), new StringContent("{}"));
                }
                catch (HttpListenerException ex)
                {
                    Interlocked.Increment(ref failures);
                    first ??= ex.Message;
                }
                finally { server?.Dispose(); }
            }
        }));
        await Task.WhenAll(loops);
        Assert.True(failures == 0, $"{failures} of 320 servers did not start: {first}");
    }
}
