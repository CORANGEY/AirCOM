using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Serial;
using AirCOM.Core.Transport;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Engine;

/// <summary>
/// Role of a bridge endpoint. A-side opens a virtual com0com port (COM11) and
/// forwards serial bytes to the network; B-side opens a real hardware port.
/// The two roles differ only in how they react to SET_PARAMS / LINE_STATE / BREAK
/// control frames; the byte-pump logic is identical.
/// </summary>
public enum BridgeRole
{
    /// <summary>A-side: virtual com0com port. Applies local params, sends SET_PARAMS to peer.</summary>
    ASide,
    /// <summary>B-side: real hardware port. Applies received SET_PARAMS to the hardware.</summary>
    BSide,
}

/// <summary>
/// The core bidirectional pump. One loop reads serial bytes, wraps them in DATA
/// frames, and writes to the framed connection. The other reads frames from the
/// connection, dispatches control frames (SET_PARAMS / LINE_STATE / BREAK) and
/// writes DATA payloads back to the serial port.
///
/// This is the most important class on the data path: every serial byte on the
/// A-side and every network frame on the B-side flows through it.
/// </summary>
public sealed class SerialBridge
{
    private readonly ISerialPort _serial;
    private readonly FramedConnection _connection;
    private readonly BridgeRole _role;
    private readonly ILogger<SerialBridge>? _logger;
    private readonly FlowStats _stats = new();

    /// <summary>Session ID stamped on every outbound frame.</summary>
    public uint SessionId { get; set; } = 1;

    /// <summary>Next outbound sequence number.</summary>
    private uint _nextSequence = 1;

    /// <summary>Optional handler invoked when a control frame arrives. Returns true if handled.</summary>
    public Func<Frame, CancellationToken, ValueTask<bool>>? ControlFrameHandler { get; set; }

    /// <summary>Periodic stats snapshot (raised roughly every StatsInterval frames or seconds).</summary>
    public event EventHandler<FlowStats>? StatsUpdated;

    /// <summary>Raised when the bridge stops (cleanly or with error).</summary>
    public event EventHandler<Exception?>? Stopped;

    private const int StatsIntervalBytes = 4096;
    private long _bytesSinceStats = 0;

    public SerialBridge(ISerialPort serial, FramedConnection connection, BridgeRole role,
        ILogger<SerialBridge>? logger = null)
    {
        _serial = serial;
        _connection = connection;
        _role = role;
        _logger = logger;
    }

    /// <summary>Runs both pump loops until cancelled or an endpoint closes.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        _stats.StartedTicks = Environment.TickCount64;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = new List<Task>();

        // Serial -> Network
        tasks.Add(Task.Run(() => PumpSerialToNetworkAsync(cts.Token), cts.Token));
        // Network -> Serial
        tasks.Add(Task.Run(() => PumpNetworkToSerialAsync(cts.Token), cts.Token));

        try
        {
            // When either pump finishes (EOF/error), cancel the other and wait for both.
            var first = await Task.WhenAny(tasks).ConfigureAwait(false);
            AirCOM.Core.Util.DiagLog.Log($"SerialBridge.RunAsync: first pump completed ({(first == tasks[0] ? "serial->net" : "net->serial")})");
            cts.Cancel();

            // Observe the first pump's result (surfaces errors, but EOF is normal).
            Exception? firstError = null;
            try { await first.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { firstError = ex; _logger?.LogError(ex, "Bridge pump failed."); }

            // IMPORTANT: do NOT await the other pump here. SerialPort.BaseStream on a
            // com0com virtual port does NOT honor CancellationToken when blocked waiting
            // for data with no input, so awaiting it would block forever -> Stopped never
            // fires -> peer-disconnect never detected. Fire-and-forget the remaining pump;
            // its cleanup (serial close, transport close) is handled by DisposeAsync.
            AirCOM.Core.Util.DiagLog.Log("SerialBridge.RunAsync: invoking Stopped (not waiting for other pump)");

            Stopped?.Invoke(this, firstError);
            AirCOM.Core.Util.DiagLog.Log($"SerialBridge.RunAsync: Stopped invoked (ex={firstError?.Message ?? "null"})");
            return;
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>Reads serial bytes and forwards them as DATA frames.</summary>
    private async Task PumpSerialToNetworkAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await _serial.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Serial read error.");
                return;
            }

            if (read == 0) continue; // com0com: 0 typically means timeout, not EOF

            var payload = read == buffer.Length ? buffer : buffer.AsSpan(0, read).ToArray();
            uint seq = _nextSequence++;
            await _connection.SendFrameAsync(Frame.Data(payload, SessionId, seq), ct).ConfigureAwait(false);

            _stats.BytesSerialToNet += read;
            _stats.FramesSent++;
            MaybeReportStats(read);
        }
    }

    /// <summary>Reads frames from the connection and dispatches them.</summary>
    private async Task PumpNetworkToSerialAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Frame? frame;
            try
            {
                frame = await _connection.ReadFrameAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Network read error.");
                return;
            }

            if (frame is null)
            {
                AirCOM.Core.Util.DiagLog.Log("SerialBridge.PumpNetworkToSerial: ReadFrameAsync returned null (EOF), returning");
                return; // EOF
            }

            _stats.FramesReceived++;

            try
            {
                switch (frame.Type)
                {
                    case FrameType.Data:
                        await _serial.WriteAsync(frame.Payload, 0, frame.Payload.Length, ct).ConfigureAwait(false);
                        _stats.BytesNetToSerial += frame.Payload.Length;
                        MaybeReportStats(frame.Payload.Length);
                        break;

                    case FrameType.SetParams:
                    case FrameType.LineState:
                    case FrameType.Break:
                    case FrameType.SetParamsAck:
                    case FrameType.Heartbeat:
                    case FrameType.Ack:
                        // Delegate control-frame handling to the role-specific host.
                        if (ControlFrameHandler is not null)
                        {
                            await ControlFrameHandler(frame, ct).ConfigureAwait(false);
                        }
                        break;

                    default:
                        _logger?.LogDebug("Unhandled frame type {Type}.", frame.Type);
                        break;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling {Type} frame.", frame.Type);
            }
        }
    }

    private void MaybeReportStats(int bytesProcessed)
    {
        _bytesSinceStats += bytesProcessed;
        if (_bytesSinceStats >= StatsIntervalBytes)
        {
            _bytesSinceStats = 0;
            StatsUpdated?.Invoke(this, _stats);
        }
    }

    public FlowStats GetCurrentStats() => _stats;
}
