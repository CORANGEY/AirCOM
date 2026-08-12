using System.Net;

namespace AirCOM.Core.Transport;

/// <summary>
/// Transport-layer abstraction for the byte stream that carries AirCOM frames.
/// A transport provides a bidirectional byte pipe; framing is layered on top
/// by <see cref="FramedConnection"/>. This decouples the bridge engine from the
/// concrete network strategy (direct TCP, P2P, relay).
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Current connection state.</summary>
    ConnectionState State { get; }

    /// <summary>Raised when the state transitions.</summary>
    event EventHandler<ConnectionState>? StateChanged;

    /// <summary>For a client transport, connects to the given endpoint. For a server-accepted transport this is a no-op.</summary>
    ValueTask ConnectAsync(EndPoint endpoint, CancellationToken ct = default);

    /// <summary>Asynchronously reads bytes into the buffer. Returns bytes read; 0 signals EOF.</summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);

    /// <summary>Asynchronously writes bytes to the transport.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>Gracefully disconnects.</summary>
    ValueTask DisconnectAsync(CancellationToken ct = default);
}
