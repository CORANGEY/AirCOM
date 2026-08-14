using System.IO;
using System.Net;
using AirCOM.Core.Engine;
using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Serial;
using AirCOM.Core.Transport;
using Microsoft.Extensions.Logging;

namespace AirCOM.App.Services;

/// <summary>
/// Connects to the AirCOM signaling/relay server and pairs via a 6-digit code.
/// After MATCHED, the connection to the server acts as the A<->B transport (the
/// server relays frames blindly), so the existing SerialBridge runs unchanged.
///
/// Both roles share this class: BSide registers the code and waits for an A;
/// ASide submits the code and connects. The engine host (AEngineHost-style
/// bridge wiring) is supplied by the caller via <see cref="OnMatchedAsync"/>.
/// </summary>
public sealed class NetworkPairingClient : IAsyncDisposable
{
    private readonly ILoggerFactory? _loggerFactory;
    private TcpTransport? _transport;
    private FramedConnection? _connection;
    private CancellationTokenSource? _cts;

    public event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>Raised with the ready FramedConnection when the server sends MATCHED.</summary>
    public event EventHandler<FramedConnection>? Matched;

    /// <summary>Raised with a human-readable failure reason.</summary>
    public event EventHandler<string>? PairingFailed;

    public NetworkPairingClient(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Connects to the server and runs the pairing handshake. Returns the matched
    /// connection (also raised via <see cref="Matched"/>). Throws on failure with
    /// a friendly message.
    /// </summary>
    public async Task<FramedConnection> PairAsync(IPEndPoint server, string pairingCode,
        bool isBSide, CancellationToken ct = default)
    {
        if (pairingCode.Length != 6 || !pairingCode.All(char.IsDigit))
            throw new ArgumentException("配对码必须是 6 位数字");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _transport = new TcpTransport(_loggerFactory?.CreateLogger<TcpTransport>());
        await _transport.ConnectAsync(server, token);
        ConnectionStateChanged?.Invoke(this, true);

        _connection = new FramedConnection(_transport, _loggerFactory?.CreateLogger<FramedConnection>());
        _connection.StartReceivePump();

        // AUTH(pairingCode, role). Role encoded in sessionId high bit: B=1, A=0
        // (same convention the server decodes).
        var auth = AuthMessage.FromPairingCode(pairingCode, (uint)(isBSide ? 0x80000001 : 1));
        var payload = auth.ToPayload();
        await _connection.SendFrameAsync(new Frame(
            new FrameHeader(FrameHeader.CurrentVersion, FrameType.Auth, 0, 0,
                (ushort)payload.Length, FrameFlags.None), payload), token);

        // Wait for MATCHED (or SIGNALING_ERROR / EOF / timeout).
        // A-side may wait up to ~10s for a B to register; B-side waits for a user
        // on the other end - give it several minutes but honor external cancel.
        var deadline = DateTime.UtcNow.Add(isBSide ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30));
        while (true)
        {
            token.ThrowIfCancellationRequested();

            Frame? frame;
            var readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            readCts.CancelAfter(TimeSpan.FromSeconds(15)); // re-loop to check deadline/cancel
            try
            {
                frame = await _connection.ReadFrameAsync(readCts.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException(isBSide ? "等待配对超时（5 分钟内无人连接）" : "配对超时：服务器上没有等待的 B 端");
                continue;
            }
            if (frame is null)
                throw new IOException("服务器断开了连接");

            switch (frame.Type)
            {
                case FrameType.Matched:
                    var sessionId = ((MatchedMessage)MessageCodec.Decode(frame)!).SessionId;
                    AirCOM.Core.Util.DiagLog.Log($"NetworkPairing: matched, session {sessionId} ({(isBSide ? "B" : "A")})");
                    Matched?.Invoke(this, _connection);
                    return _connection;

                case FrameType.SignalingError:
                    var err = (SignalingErrorMessage)MessageCodec.Decode(frame)!;
                    var reason = err.ErrorCode switch
                    {
                        SignalingErrorMessage.CodeBadPairingCode => "配对码无效或已被使用",
                        SignalingErrorMessage.CodePairingTimeout => "配对等待超时",
                        _ => $"服务器错误 {err.ErrorCode}: {err.Message}",
                    };
                    PairingFailed?.Invoke(this, reason);
                    throw new InvalidOperationException(reason);

                default:
                    // Ignore stray frames during pairing.
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_connection is not null) await _connection.DisposeAsync();
        _transport?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}
