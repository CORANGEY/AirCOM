using System.IO.Ports;
using AirCOM.Core.Protocol;
using AirCOM.Core.Util;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Serial;

/// <summary>
/// A real hardware serial port (B-side). Wraps <see cref="System.IO.Ports.SerialPort"/>
/// for data and uses Win32 P/Invoke for the control-line signals it doesn't expose.
/// </summary>
public sealed class RealSerialPort : ISerialPort
{
    private readonly ILogger<RealSerialPort>? _logger;
    private SerialPort? _port;
    private IntPtr _handle = IntPtr.Zero;
    private Thread? _monitorThread;
    private CancellationTokenSource? _monitorCts;
    private ModemStatusFlags _lastStatus;
    private SerialParams _currentParams;
    private volatile bool _disposed;

    public string PortName { get; }
    public int BytesToRead => _port?.BytesToRead ?? 0;
    public bool IsOpen => _port?.IsOpen ?? false;

    public event EventHandler<ModemStatusFlags>? ControlLinesChanged;

    public RealSerialPort(string portName, ILogger<RealSerialPort>? logger = null)
    {
        PortName = portName;
        _logger = logger;
    }

    public Task OpenAsync(SerialParams parameters, CancellationToken ct = default)
    {
        if (IsOpen) throw new InvalidOperationException($"Port {PortName} already open.");

        var sp = new SerialPort(PortName)
        {
            BaudRate = (int)(parameters.BaudRate == SerialParams.UnknownBaudRate ? 115200 : parameters.BaudRate),
            DataBits = parameters.DataBits == 0 ? 8 : parameters.DataBits,
            Parity = MapParity(parameters.Parity),
            StopBits = MapStopBits(parameters.StopBits),
            Handshake = MapFlowControl(parameters.FlowControl),
            ReadTimeout = Timeout.Infinite,
            WriteTimeout = Timeout.Infinite,
            ReadBufferSize = 65536,
            WriteBufferSize = 65536,
        };
        sp.Open();
        _port = sp;
        _currentParams = parameters;
        AirCOM.Core.Util.DiagLog.Log($"RealSerialPort.OpenAsync: opened {PortName} @ {sp.BaudRate}");

        // NOTE: previously we opened a SECOND handle via CreateFile for control-line
        // P/Invoke. That second handle racing with SerialPort.BaseStream's internal
        // I/O thread causes intermittent IOException "I/O operation has been aborted
        // because of a thread exit" on real serial ports -> instant disconnect.
        // Control-line support is now degraded (DTR/RTS via SerialPort properties;
        // input lines DCD/CTS/DSR/RI return None) until a safer approach is added.
        _handle = IntPtr.Zero;

        // Start the control-line monitor.
        _lastStatus = GetControlLines();
        _monitorCts = new CancellationTokenSource();
        _monitorThread = new Thread(() => MonitorLoop(_monitorCts.Token))
        {
            IsBackground = true,
            Name = $"RealSerialPort:{PortName}"
        };
        _monitorThread.Start();

        return Task.CompletedTask;
    }

    public void SetParams(SerialParams parameters)
    {
        if (_port is null || !IsOpen)
            throw new InvalidOperationException($"Port {PortName} not open.");

        AirCOM.Core.Util.DiagLog.Log($"RealSerialPort.SetParams: called baud={parameters.BaudRate} data={parameters.DataBits} (current baud={_currentParams.BaudRate})");
        // Skip if params unchanged. Changing SerialPort BaudRate/DataBits/Parity
        // REOPENS the underlying serial handle, which aborts any in-flight
        // BaseStream.ReadAsync with IOException "I/O operation has been aborted
        // because of a thread exit" -> the bridge pump dies -> instant disconnect.
        if (ParamsEqual(_currentParams, parameters))
        {
            AirCOM.Core.Util.DiagLog.Log("RealSerialPort.SetParams: params unchanged, skipping");
            return;
        }
        AirCOM.Core.Util.DiagLog.Log("RealSerialPort.SetParams: params DIFFERENT, applying (will reset port)");

        _port.BaudRate = (int)(parameters.BaudRate == SerialParams.UnknownBaudRate ? 115200 : parameters.BaudRate);
        _port.DataBits = parameters.DataBits == 0 ? 8 : parameters.DataBits;
        _port.Parity = MapParity(parameters.Parity);
        _port.StopBits = MapStopBits(parameters.StopBits);
        _port.Handshake = MapFlowControl(parameters.FlowControl);
        _currentParams = parameters;
    }

    private static bool ParamsEqual(SerialParams a, SerialParams b)
    {
        uint ab = a.BaudRate == SerialParams.UnknownBaudRate ? 115200 : a.BaudRate;
        uint bb = b.BaudRate == SerialParams.UnknownBaudRate ? 115200 : b.BaudRate;
        return ab == bb
            && (a.DataBits == 0 ? 8 : a.DataBits) == (b.DataBits == 0 ? 8 : b.DataBits)
            && a.StopBits == b.StopBits
            && a.Parity == b.Parity
            && a.FlowControl == b.FlowControl;
    }

    public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        if (_port is null || !IsOpen) return 0;
        // IMPORTANT: do NOT use _port.BaseStream.ReadAsync. On real serial ports it
        // internally relies on a background I/O thread that intermittently dies and
        // throws IOException "I/O operation has been aborted because of a thread exit",
        // even though the port is fine -> bridge pump dies -> instant disconnect.
        // The synchronous SerialPort.Read is reliable. Run it off the thread pool with
        // a finite ReadTimeout so cancellation (via re-loop / ct) works.
        try
        {
            int n = await Task.Run(() =>
            {
                // SerialPort.Read blocks until data or ReadTimeout. Use a short timeout
                // so we re-check ct regularly.
                _port.ReadTimeout = 200;
                try { return _port.Read(buffer, offset, count); }
                catch (TimeoutException) { return 0; }
            }, ct).ConfigureAwait(false);
            if (n > 0) AirCOM.Core.Util.DiagLog.Log($"RealSerialPort.ReadAsync: read {n} bytes");
            return n;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            AirCOM.Core.Util.DiagLog.Log($"RealSerialPort.ReadAsync: exception {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        if (_port is null || !IsOpen) return;
        // Use synchronous Write off the thread pool (BaseStream.WriteAsync has the same
        // I/O-thread-abort problem as ReadAsync).
        try
        {
            await Task.Run(() => _port.Write(buffer, offset, count), ct).ConfigureAwait(false);
            AirCOM.Core.Util.DiagLog.Log($"RealSerialPort.WriteAsync: wrote {count} bytes");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AirCOM.Core.Util.DiagLog.Log($"RealSerialPort.WriteAsync: exception {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    public void SetControlLines(ModemStatusFlags output)
    {
        if (_port is null) return;
        // Use SerialPort's own properties (no second handle). DTR/RTS only.
        _port.DtrEnable = (output & ModemStatusFlags.Dtr) != 0;
        _port.RtsEnable = (output & ModemStatusFlags.Rts) != 0;
    }

    public ModemStatusFlags GetControlLines()
    {
        if (_port is null) return ModemStatusFlags.None;
        // Without the second Win32 handle, we can only read output lines (DTR/RTS).
        // Input lines (DCD/CTS/DSR/RI) require GetCommModemStatus on a handle we
        // no longer keep open, to avoid the I/O-thread-abort conflict. Return None
        // for inputs until a safer control-line path is added.
        ModemStatusFlags flags = ModemStatusFlags.None;
        if (_port.DtrEnable) flags |= ModemStatusFlags.Dtr;
        if (_port.RtsEnable) flags |= ModemStatusFlags.Rts;
        return flags;
    }

    public void Break(ushort durationMs)
    {
        if (_handle == IntPtr.Zero) return;
        Win32Api.EscapeCommFunction(_handle, Win32Api.SETBREAK);
        Thread.Sleep(durationMs);
        Win32Api.EscapeCommFunction(_handle, Win32Api.CLRBREAK);
    }

    /// <summary>Background monitor that raises ControlLinesChanged on transitions.</summary>
    private void MonitorLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _handle != IntPtr.Zero)
        {
            try
            {
                Thread.Sleep(50); // poll interval; event-driven alternative via WaitCommEvent can be added later
                var current = GetControlLines() & ModemStatusFlags.InputMask;
                var prev = _lastStatus & ModemStatusFlags.InputMask;
                if (current != prev)
                {
                    _lastStatus = (_lastStatus & ~ModemStatusFlags.InputMask) | current;
                    ControlLinesChanged?.Invoke(this, current);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Control-line monitor error on {Port}.", PortName);
                break;
            }
        }
    }

    private static System.IO.Ports.Parity MapParity(Protocol.Parity p) => p switch
    {
        Protocol.Parity.None => System.IO.Ports.Parity.None,
        Protocol.Parity.Odd => System.IO.Ports.Parity.Odd,
        Protocol.Parity.Even => System.IO.Ports.Parity.Even,
        Protocol.Parity.Mark => System.IO.Ports.Parity.Mark,
        Protocol.Parity.Space => System.IO.Ports.Parity.Space,
        _ => System.IO.Ports.Parity.None,
    };

    private static System.IO.Ports.StopBits MapStopBits(StopBitsKind s) => s switch
    {
        StopBitsKind.One => System.IO.Ports.StopBits.One,
        StopBitsKind.OnePointFive => System.IO.Ports.StopBits.OnePointFive,
        StopBitsKind.Two => System.IO.Ports.StopBits.Two,
        _ => System.IO.Ports.StopBits.One,
    };

    private static System.IO.Ports.Handshake MapFlowControl(FlowControl f) => f switch
    {
        FlowControl.Hardware => System.IO.Ports.Handshake.RequestToSend,
        FlowControl.Software => System.IO.Ports.Handshake.XOnXOff,
        _ => System.IO.Ports.Handshake.None,
    };

    /// <summary>Opens a native handle to the COM port for control-line access (read-only, shared).</summary>
    private static IntPtr OpenPortHandle(string portName)
    {
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 0x00000001;
        const uint FILE_SHARE_WRITE = 0x00000002;
        const uint OPEN_EXISTING = 3;
        return Win32Api.CreateFile(
            portName, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitorCts?.Cancel();
        _monitorCts?.Dispose();

        try { _port?.Close(); } catch { /* swallow on dispose */ }
        _port?.Dispose();
        _port = null;
        if (_handle != IntPtr.Zero)
        {
            Win32Api.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
