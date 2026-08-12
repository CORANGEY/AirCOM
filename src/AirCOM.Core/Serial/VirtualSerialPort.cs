using System.IO.Ports;
using AirCOM.Core.Protocol;
using AirCOM.Core.Util;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Serial;

/// <summary>
/// A com0com virtual serial port (A-side COM11). Uses the same SerialPort wrapper
/// as the real port for data, and Win32 P/Invoke to read control-line signals that
/// com0com maps from the peer side. DTR/RTS written here are mirrored to the peer
/// (COM10) for the user's application to read.
/// </summary>
public sealed class VirtualSerialPort : ISerialPort
{
    private readonly ILogger<VirtualSerialPort>? _logger;
    private SerialPort? _port;
    private IntPtr _handle = IntPtr.Zero;
    private Thread? _monitorThread;
    private CancellationTokenSource? _monitorCts;
    private ModemStatusFlags _lastStatus;
    private volatile bool _disposed;

    public string PortName { get; }
    public bool IsOpen => _port?.IsOpen ?? false;

    public event EventHandler<ModemStatusFlags>? ControlLinesChanged;

    public VirtualSerialPort(string portName, ILogger<VirtualSerialPort>? logger = null)
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
            Handshake = System.IO.Ports.Handshake.None, // virtual port: no flow control
            ReadTimeout = Timeout.Infinite,
            WriteTimeout = Timeout.Infinite,
            ReadBufferSize = 65536,
            WriteBufferSize = 65536,
        };
        sp.Open();
        _port = sp;
        _handle = OpenPortHandle(PortName);

        _lastStatus = GetControlLines();
        _monitorCts = new CancellationTokenSource();
        _monitorThread = new Thread(() => MonitorLoop(_monitorCts.Token))
        {
            IsBackground = true,
            Name = $"VirtualSerialPort:{PortName}"
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
        if (_port is null) return;

        // On the virtual port we set DTR/RTS via the SerialPort wrapper; com0com's
        // control-line mapping reflects these to the peer (COM10).
        _port.DtrEnable = (output & ModemStatusFlags.Dtr) != 0;
        _port.RtsEnable = (output & ModemStatusFlags.Rts) != 0;
    }

    public ModemStatusFlags GetControlLines()
    {
        if (_handle == IntPtr.Zero) return ModemStatusFlags.None;

        Win32Api.GetCommModemStatus(_handle, out uint status);

        ModemStatusFlags flags = ModemStatusFlags.None;
        if (_port != null)
        {
            if (_port.DtrEnable) flags |= ModemStatusFlags.Dtr;
            if (_port.RtsEnable) flags |= ModemStatusFlags.Rts;
        }
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

    private void MonitorLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _handle != IntPtr.Zero)
        {
            try
            {
                Thread.Sleep(50);
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
                _logger?.LogWarning(ex, "Control-line monitor error on virtual {Port}.", PortName);
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
