using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Serial;
using AirCOM.Core.Transport;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Engine;

/// <summary>
/// Polls the local serial port's input control lines (DCD/CTS/DSR/RI) and sends
/// a LINE_STATE frame to the peer whenever they change. Also applies incoming
/// LINE_STATE frames from the peer to the local output lines (DTR/RTS).
///
/// Used by the host (A or B) which wires it into the SerialBridge's
/// ControlFrameHandler. Polling interval is conservative (50ms); an event-driven
/// variant using WaitCommEvent could be added later.
/// </summary>
public sealed class ControlLinePoller : IDisposable
{
    private readonly ISerialPort _serial;
    private readonly FramedConnection _connection;
    private readonly ILogger<ControlLinePoller>? _logger;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private ModemStatusFlags _lastInput;
    private readonly Func<uint> _nextSequence;

    /// <summary>Interval between polls, in ms.</summary>
    public int PollIntervalMs { get; set; } = 50;

    public ControlLinePoller(ISerialPort serial, FramedConnection connection,
        Func<uint> nextSequence, ILogger<ControlLinePoller>? logger = null)
    {
        _serial = serial;
        _connection = connection;
        _nextSequence = nextSequence;
        _logger = logger;
    }

    /// <summary>Starts the polling thread.</summary>
    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        _lastInput = _serial.GetControlLines() & ModemStatusFlags.InputMask;
        _thread = new Thread(() => PollLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "ControlLinePoller"
        };
        _thread.Start();
    }

    /// <summary>
    /// Applies an incoming LINE_STATE frame from the peer to the local port's
    /// output lines (DTR/RTS). Called from the bridge's network pump.
    /// </summary>
    public async ValueTask ApplyPeerLineStateAsync(Frame frame, CancellationToken ct)
    {
        var msg = (LineStateMessage)MessageCodec.Decode(frame)!;
        // Peer's output lines (DTR/RTS) become our output lines to drive locally.
        var output = msg.OutputLines & ModemStatusFlags.OutputMask;
        if (output == ModemStatusFlags.None && msg.ChangeMask == ModemStatusFlags.None) return;

        try { _serial.SetControlLines(output); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to apply peer line state."); }

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    private void PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Thread.Sleep(PollIntervalMs);
                var current = _serial.GetControlLines() & ModemStatusFlags.InputMask;
                if (current != _lastInput)
                {
                    var changed = current ^ _lastInput;
                    _lastInput = current;
                    var msg = new LineStateMessage(
                        output: ModemStatusFlags.None,
                        input: current,
                        changeMask: changed & ModemStatusFlags.InputMask);
                    var payload = msg.ToPayload();
                    var frame = new Frame(
                        new FrameHeader(FrameHeader.CurrentVersion, FrameType.LineState,
                            1, _nextSequence(), (ushort)payload.Length, FrameFlags.None),
                        payload);
                    _connection.SendFrameAsync(frame, ct).AsTask().GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Control-line poll error.");
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        try { _thread?.Join(1000); } catch { }
    }
}
