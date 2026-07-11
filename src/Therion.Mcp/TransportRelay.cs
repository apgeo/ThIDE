using System.Threading.Channels;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp;

/// <summary>
/// Pumps JSON-RPC messages verbatim between two transports, both directions, until either side ends.
/// The <c>--connect</c> shim uses it to bridge a stdio-only host to the running IDE's HTTP server: it
/// forwards <c>initialize</c>, <c>tools/*</c>, notifications and progress untouched, so the host and the
/// in-app server negotiate the protocol directly and the shim stays a dumb, transport-neutral pipe with
/// no catalog of its own to drift out of sync.
/// </summary>
public static class TransportRelay
{
    /// <summary>
    /// Relays until one transport closes (EOF on the host's stdin, or the server dropping) or
    /// <paramref name="ct"/> is cancelled. The caller disposes both transports afterwards, which
    /// unblocks whichever pump is still waiting.
    /// </summary>
    public static async Task RunAsync(ITransport a, ITransport b, CancellationToken ct)
    {
        var aToB = PumpAsync(a, b, ct);
        var bToA = PumpAsync(b, a, ct);
        await Task.WhenAny(aToB, bToA).ConfigureAwait(false);
    }

    private static async Task PumpAsync(ITransport from, ITransport to, CancellationToken ct)
    {
        try
        {
            await foreach (var message in from.MessageReader.ReadAllAsync(ct).ConfigureAwait(false))
                await to.SendMessageAsync(message, ct).ConfigureAwait(false);
        }
        // A pipe ends for exactly these reasons: cancellation, a completed/closed channel, or the
        // destination transport already being torn down. None is an error worth crashing the shim over.
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
}
