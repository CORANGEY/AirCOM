using System.Net;
using System.Net.Sockets;
using AirCOM.Core.Engine;
using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Serial;
using AirCOM.Core.Transport;
using Microsoft.Extensions.Logging;

namespace AirCOM.Client.B.Services;

/// <summary>
/// B-side engine host. Listens for an incoming TCP connection from the A-side,
/// opens the real hardware serial port the user selected, and runs the SerialBridge
/// to forward bytes bidirectionally. Applies SET_PARAMS / LINE_STATE / BREAK frames
/// received from the A-side to the hardware.
/// </summary>
public sealed class BEngineHost : IAsyncDisposable
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<BEngineHost>? _logger;
    private TcpListener? _listener;
    private RealSerialPort? _serial;
    private FramedConnection? _connection;
    private SerialBridge? _bridge;
    private ControlLinePoller? _poller;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    /// <summary>Serial port name the user picked on the B-side (e.g. "COM3").</summary>
    public string? PortName { get; private set; }

    /// <summary>Raised when the bridge reports a stats snapshot.</summary>
    public event EventHandler<FlowStats>? StatsUpdated;

    /// <summary>Raised when the bridge stops (cleanly or with error).</summary>
    public event EventHandler<Exception?>? Stopped;

    /// <summary>Raised when a client connects or disconnects.</summary>
    public event EventHandler<bool>? ConnectionStateChanged;

    public BEngineHost(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<BEngineHost>();
    }

    /// <summary>Starts listening on the given port and waits for one A-side connection.</summary>
    public async Task StartAsync(string portName, SerialParams initialParams, int listenPort, CancellationToken ct = default)
    {
        PortName = portName;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Open the real serial port.
        _serial = new RealSerialPort(portName, _loggerFactory?.CreateLogger<RealSerialPort>());
        await _serial.OpenAsync(initialParams, ct);
        _serial.ControlLinesChanged += OnLocalControlLinesChanged;

        // Listen for the A-side.
        _listener = new TcpListener(IPAddress.Any, listenPort);
        _listener.Start();
        _logger?.LogInformation("B-side listening on port {Port} for serial port {Serial}.", listenPort, portName);

        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Accept failed.");
                return;
            }

            ConnectionStateChanged?.Invoke(this, true);
            try
            {
                var transport = new TcpTransport(client, _loggerFactory?.CreateLogger<TcpTransport>());
                _connection = new FramedConnection(transport, _loggerFactory?.CreateLogger<FramedConnection>());
                _connection.StartReceivePump();
                AirCOM.Core.Util.DiagLog.Log("BEngineHost: A-side connected, starting bridge");

                _poller = new ControlLinePoller(_serial!, _connection, NextSequence,
                    _loggerFactory?.CreateLogger<ControlLinePoller>());

                _bridge = new SerialBridge(_serial!, _connection, BridgeRole.BSide,
                    _loggerFactory?.CreateLogger<SerialBridge>());
                _bridge.ControlFrameHandler = HandleControlFrameAsync;
                _bridge.StatsUpdated += (s, stats) => StatsUpdated?.Invoke(s, stats);
                _bridge.Stopped += (s, ex) =>
                {
                    Stopped?.Invoke(s, ex);
                    ConnectionStateChanged?.Invoke(this, false);
                };

                _poller.Start();
                await _bridge.RunAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Bridge run failed.");
                Stopped?.Invoke(this, ex);
            }
            finally
            {
                ConnectionStateChanged?.Invoke(this, false);
                _poller?.Dispose();
                if (_connection is not null) await _connection.DisposeAsync();
                _connection = null;
                _bridge = null;
                AirCOM.Core.Util.DiagLog.Log("BEngineHost: bridge stopped, going back to accept next A-side");
            }
        }
    }

    /// <summary>Handles SET_PARAMS / LINE_STATE / BREAK frames from the A-side.</summary>
    private async ValueTask<bool> HandleControlFrameAsync(Frame frame, CancellationToken ct)
    {
        switch (frame.Type)
        {
            case FrameType.SetParams:
                var sp = (SetParamsMessage)MessageCodec.Decode(frame)!;
                try { _serial!.SetParams(sp.Params); }
                catch (Exception ex) { _logger?.LogWarning(ex, "SetParams failed."); }
                // Ack back to the A-side.
                var ackPayload = new SetParamsAckMessage(ok: true).ToPayload();
                await _connection!.SendFrameAsync(new Frame(
                    new FrameHeader(FrameHeader.CurrentVersion, FrameType.SetParamsAck,
                        1, NextSequence(), (ushort)ackPayload.Length, FrameFlags.None), ackPayload), ct);
                return true;

            case FrameType.LineState:
                await _poller!.ApplyPeerLineStateAsync(frame, ct);
                return true;

            case FrameType.Break:
                var brk = (BreakMessage)MessageCodec.Decode(frame)!;
                _serial!.Break(brk.DurationMs);
                return true;

            default:
                return false;
        }
    }

    private void OnLocalControlLinesChanged(object? sender, ModemStatusFlags input)
    {
        // The poller already sends LINE_STATE frames; this handler is a hook for
        // future low-latency event-driven sending if we move off polling.
    }

    private uint _nextSeq = 1;
    private uint NextSequence() => _nextSeq++;

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _poller?.Dispose();
        if (_connection is not null) await _connection.DisposeAsync();
        _listener?.Stop();
        _serial?.Dispose();
        _cts?.Dispose();
    }
}
