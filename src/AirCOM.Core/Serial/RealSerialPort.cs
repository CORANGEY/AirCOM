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
    private volatile bool _disposed;

    public string PortName { get; }
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

        // Open a second handle to the same device for control-line P/Invoke operations.
        // SerialPort.BaseStream's internal handle isn't reliably accessible across versions.
        _handle = OpenPortHandle(PortName);

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

        _port.BaudRate = (int)(parameters.BaudRate == SerialParams.UnknownBaudRate ? 115200 : parameters.BaudRate);
        _port.DataBits = parameters.DataBits == 0 ? 8 : parameters.DataBits;
        _port.Parity = MapParity(parameters.Parity);
        _port.StopBits = MapStopBits(parameters.StopBits);
        _port.Handshake = MapFlowControl(parameters.FlowControl);
    }

    public async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        if (_port is null || !IsOpen) return 0;
        var stream = _port.BaseStream;
        return await stream.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
    }

    public async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        if (_port is null || !IsOpen) return;
        var stream = _port.BaseStream;
        await stream.WriteAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void SetControlLines(ModemStatusFlags output)
    {
        if (_handle == IntPtr.Zero) return;

        if ((output & ModemStatusFlags.Dtr) != 0)
            Win32Api.EscapeCommFunction(_handle, Win32Api.SETDTR);
        else
            Win32Api.EscapeCommFunction(_handle, Win32Api.CLRDTR);

        if ((output & ModemStatusFlags.Rts) != 0)
            Win32Api.EscapeCommFunction(_handle, Win32Api.SETRTS);
        else
            Win32Api.EscapeCommFunction(_handle, Win32Api.CLRRTS);
    }

    public ModemStatusFlags GetControlLines()
    {
        if (_handle == IntPtr.Zero) return ModemStatusFlags.None;

        Win32Api.GetCommModemStatus(_handle, out uint status);

        // Output lines: DTR/RTS aren't returned by GetCommModemStatus, so read from the port wrapper.
        ModemStatusFlags flags = ModemStatusFlags.None;
        if (_port != null)
        {
            if (_port.DtrEnable) flags |= ModemStatusFlags.Dtr;
            if (_port.RtsEnable) flags |= ModemStatusFlags.Rts;
        }

        // Input lines from modem status.
        if ((status & Win32Api.MS_RLSD_ON) != 0) flags |= ModemStatusFlags.Dcd;
        if ((status & Win32Api.MS_CTS_ON) != 0) flags |= ModemStatusFlags.Cts;
        if ((status & Win32Api.MS_DSR_ON) != 0) flags |= ModemStatusFlags.Dsr;
        if ((status & Win32Api.MS_RING_ON) != 0) flags |= ModemStatusFlags.Ri;

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
