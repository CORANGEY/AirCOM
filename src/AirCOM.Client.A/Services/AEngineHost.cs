using System.Net;
using AirCOM.Core.Engine;
using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Serial;
using AirCOM.Core.Transport;
using Microsoft.Extensions.Logging;

namespace AirCOM.Client.A.Services;

/// <summary>
/// A-side engine host. Opens the com0com virtual port (COM11), connects to the
/// B-side over TCP, and runs the SerialBridge so the user's serial app on COM10
/// sees the remote device. Sends SET_PARAMS to the B-side whenever the local
/// virtual port parameters change, and forwards DTR/RTS as LINE_STATE.
/// </summary>
public sealed class AEngineHost : IAsyncDisposable
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<AEngineHost>? _logger;
    private VirtualSerialPort? _serial;
    private TcpTransport? _transport;
    private FramedConnection? _connection;
    private SerialBridge? _bridge;
    private ControlLinePoller? _poller;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    /// <summary>Name of the com0com virtual port driven by the service (default COM11).</summary>
    public string VirtualPortName { get; private set; } = "COM11";

    /// <summary>Raised on stats snapshots.</summary>
    public event EventHandler<FlowStats>? StatsUpdated;

    /// <summary>Raised when the bridge stops.</summary>
    public event EventHandler<Exception?>? Stopped;

    /// <summary>Raised on connect/disconnect.</summary>
    public event EventHandler<bool>? ConnectionStateChanged;

    public AEngineHost(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<AEngineHost>();
    }

    /// <summary>Connects to the B-side and starts the bridge.</summary>
    public async Task StartAsync(string virtualPortName, SerialParams initialParams,
        IPAddress host, int port, CancellationToken ct = default)
    {
        VirtualPortName = virtualPortName;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Open the com0com virtual port.
        _serial = new VirtualSerialPort(virtualPortName, _loggerFactory?.CreateLogger<VirtualSerialPort>());
        await _serial.OpenAsync(initialParams, ct);

        // Connect to the B-side.
        _transport = new TcpTransport(_loggerFactory?.CreateLogger<TcpTransport>());
        await _transport.ConnectAsync(new IPEndPoint(host, port), ct);
        ConnectionStateChanged?.Invoke(this, true);

        _connection = new FramedConnection(_transport, _loggerFactory?.CreateLogger<FramedConnection>());
        _connection.StartReceivePump();

        _poller = new ControlLinePoller(_serial, _connection, NextSequence,
            _loggerFactory?.CreateLogger<ControlLinePoller>());

        _bridge = new SerialBridge(_serial, _connection, BridgeRole.ASide,
            _loggerFactory?.CreateLogger<SerialBridge>());
        _bridge.ControlFrameHandler = HandleControlFrameAsync;
        _bridge.StatsUpdated += (s, stats) => StatsUpdated?.Invoke(s, stats);
        _bridge.Stopped += (s, ex) =>
        {
            AirCOM.Core.Util.DiagLog.Log($"AEngineHost: bridge Stopped event (ex={ex?.Message ?? "null"})");
            Stopped?.Invoke(s, ex);
            ConnectionStateChanged?.Invoke(this, false);
        };

        // Push initial params to the B-side so the hardware matches the virtual port.
        await SendSetParamsAsync(initialParams, ct);

        _poller.Start();
        _runTask = Task.Run(() => _bridge.RunAsync(_cts.Token), _cts.Token);
    }

    /// <summary>Sends a SET_PARAMS frame to the B-side when local params change.</summary>
    public async ValueTask SendSetParamsAsync(SerialParams parameters, CancellationToken ct = default)
    {
        if (_connection is null) return;
        var payload = new SetParamsMessage(parameters).ToPayload();
        await _connection.SendFrameAsync(new Frame(
            new FrameHeader(FrameHeader.CurrentVersion, FrameType.SetParams,
                1, NextSequence(), (ushort)payload.Length, FrameFlags.None), payload), ct);
    }

    /// <summary>Handles LINE_STATE / SET_PARAMS_ACK from the B-side.</summary>
    private async ValueTask<bool> HandleControlFrameAsync(Frame frame, CancellationToken ct)
    {
        switch (frame.Type)
        {
            case FrameType.LineState:
                await _poller!.ApplyPeerLineStateAsync(frame, ct);
                return true;
            case FrameType.SetParamsAck:
                var ack = (SetParamsAckMessage)MessageCodec.Decode(frame)!;
                if (!ack.IsOk) _logger?.LogWarning("B-side rejected SET_PARAMS (code {Code}).", ack.ErrorCode);
                return true;
            default:
                return false;
        }
    }

    private uint _nextSeq = 1;
    private uint NextSequence() => _nextSeq++;

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _poller?.Dispose();
        if (_connection is not null) await _connection.DisposeAsync();
        _transport?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _serial?.Dispose();
        _cts?.Dispose();
    }
}
