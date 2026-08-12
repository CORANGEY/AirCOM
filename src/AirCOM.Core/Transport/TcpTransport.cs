using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Transport;

/// <summary>
/// Direct TCP transport for M1 (LAN). Wraps a <see cref="TcpClient"/> / accepted
/// <see cref="NetworkStream"/>. Used both as a client (A-side connects to B) and as
/// a server-accepted connection (B-side accepts). Streaming is unframed raw bytes;
/// <see cref="FramedConnection"/> adds frame encoding on top.
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly ILogger<TcpTransport>? _logger;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ConnectionState _state = ConnectionState.Disconnected;

    public ConnectionState State => _state;

    public event EventHandler<ConnectionState>? StateChanged;

    /// <summary>Constructs a client transport (will call ConnectAsync to establish the socket).</summary>
    public TcpTransport(ILogger<TcpTransport>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>Constructs a transport over an already-accepted client (server side).</summary>
    public TcpTransport(TcpClient acceptedClient, ILogger<TcpTransport>? logger = null)
    {
        _logger = logger;
        _client = acceptedClient;
        _stream = acceptedClient.GetStream();
        SetState(ConnectionState.Connected);
    }

    public async ValueTask ConnectAsync(EndPoint endpoint, CancellationToken ct = default)
    {
        if (_state == ConnectionState.Connected)
            throw new InvalidOperationException("Already connected.");

        SetState(ConnectionState.Connecting);
        try
        {
            _client = new TcpClient();
            // TcpClient.ConnectAsync has overloads for (IPAddress,int), (IPEndPoint), and (string host,int port),
            // all of which accept a CancellationToken in .NET 8. Resolve based on the endpoint type.
            switch (endpoint)
            {
                case IPEndPoint ip:
                    await _client.ConnectAsync(ip.Address, ip.Port, ct).ConfigureAwait(false);
                    break;
                case DnsEndPoint dns:
                    await _client.ConnectAsync(dns.Host, dns.Port, ct).ConfigureAwait(false);
                    break;
                default:
                    // Fall back to the socket-level connect for unusual endpoint types.
                    await _client.Client.ConnectAsync(endpoint, ct).ConfigureAwait(false);
                    break;
            }
            _stream = _client.GetStream();
            SetState(ConnectionState.Connected);
        }
        catch
        {
            SetState(ConnectionState.Disconnected);
            _client?.Dispose();
            _client = null;
            throw;
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected.");
        return await _stream.ReadAsync(buffer, ct).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_stream is null)
            throw new InvalidOperationException("Not connected.");
        await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public ValueTask DisconnectAsync(CancellationToken ct = default)
    {
        if (_state == ConnectionState.Disconnected) return ValueTask.CompletedTask;
        SetState(ConnectionState.Disconnecting);
        try
        {
            _stream?.Close();
            _client?.Close();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error closing TcpTransport.");
        }
        finally
        {
            _stream = null;
            _client = null;
            SetState(ConnectionState.Disconnected);
        }
        return ValueTask.CompletedTask;
    }

    private void SetState(ConnectionState newState)
    {
        if (_state == newState) return;
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    public ValueTask DisposeAsync()
    {
        try { _stream?.Dispose(); } catch { }
        _client?.Dispose();
        _stream = null;
        _client = null;
        SetState(ConnectionState.Disconnected);
        return ValueTask.CompletedTask;
    }
}
