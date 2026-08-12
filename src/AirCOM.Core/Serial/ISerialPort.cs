using AirCOM.Core.Protocol;
using AirCOM.Core.Serial;

namespace AirCOM.Core.Serial;

/// <summary>
/// Transport-agnostic serial port abstraction. Both the real hardware port (B-side,
/// backed by <c>System.IO.Ports.SerialPort</c>) and the com0com virtual port (A-side
/// COM11) implement this, letting the bridge engine treat them identically.
/// </summary>
public interface ISerialPort : IDisposable
{
    /// <summary>Whether the port is currently open.</summary>
    bool IsOpen { get; }

    /// <summary>Port name, e.g. "COM3" or "COM11".</summary>
    string PortName { get; }

    /// <summary>Opens the port with the given parameters.</summary>
    Task OpenAsync(SerialParams parameters, CancellationToken ct = default);

    /// <summary>Applies new serial parameters to an open port.</summary>
    void SetParams(SerialParams parameters);

    /// <summary>Asynchronously reads serial data into the buffer. Returns bytes read (0 = timeout/EOF).</summary>
    Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default);

    /// <summary>Writes data to the serial port.</summary>
    Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct = default);

    /// <summary>Sets the output control lines (DTR/RTS).</summary>
    void SetControlLines(ModemStatusFlags output);

    /// <summary>Reads the current control-line status (both output and input lines).</summary>
    ModemStatusFlags GetControlLines();

    /// <summary>Raises the serial break signal for the given duration.</summary>
    void Break(ushort durationMs);

    /// <summary>Raised when an input control line (DCD/CTS/DSR/RI) changes state.</summary>
    event EventHandler<ModemStatusFlags>? ControlLinesChanged;
}
