// T-03.6: the stdio<->HTTP shim's pump. A fake pair of transports proves messages cross verbatim in
// both directions and that cancellation ends the relay — the transparent-pipe contract, without a real
// socket or Console.

using System.Diagnostics;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Therion.Mcp.Tests;

public class TransportRelayTests
{
    /// <summary>An ITransport whose inbound channel a test drives, recording everything sent to it.</summary>
    private sealed class FakeTransport : ITransport
    {
        private readonly Channel<JsonRpcMessage> _incoming = Channel.CreateUnbounded<JsonRpcMessage>();
        public List<JsonRpcMessage> Sent { get; } = [];

        public string? SessionId => null;
        public ChannelReader<JsonRpcMessage> MessageReader => _incoming.Reader;

        public void Deliver(JsonRpcMessage message) => _incoming.Writer.TryWrite(message);
        public void Close() => _incoming.Writer.TryComplete();

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Messages_cross_verbatim_in_both_directions()
    {
        var host = new FakeTransport();
        var server = new FakeTransport();
        var relay = TransportRelay.RunAsync(host, server, CancellationToken.None);

        var fromHost = new JsonRpcNotification { Method = "from/host" };
        var fromServer = new JsonRpcNotification { Method = "from/server" };
        host.Deliver(fromHost);       // host -> server
        server.Deliver(fromServer);   // server -> host

        await WaitUntilAsync(() => server.Sent.Count >= 1 && host.Sent.Count >= 1);

        Assert.Same(fromHost, Assert.Single(server.Sent));   // forwarded, same instance (verbatim)
        Assert.Same(fromServer, Assert.Single(host.Sent));

        host.Close();          // EOF on one side ends the relay
        await relay;
    }

    [Fact]
    public async Task Cancellation_ends_the_relay()
    {
        using var cts = new CancellationTokenSource();
        var relay = TransportRelay.RunAsync(new FakeTransport(), new FakeTransport(), cts.Token);

        Assert.False(relay.IsCompleted);   // both idle, blocked on their readers
        cts.Cancel();

        await relay;   // returns rather than hanging
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "condition was not met before the timeout.");
    }
}
