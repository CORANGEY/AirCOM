namespace AirCOM.Core.Serial;

/// <summary>
/// Modem control-line status flags. Modeled on the Win32 GetCommModemStatus bits
/// so the value can be passed through with minimal translation.
/// </summary>
[Flags]
public enum ModemStatusFlags : byte
{
    None = 0,

    // Output lines (controlled by the local side): DTR / RTS
    Dtr = 1 << 0,
    Rts = 1 << 1,

    // Input lines (read from the remote device): DCD / CTS / DSR / RI
    Dcd = 1 << 2,
    Cts = 1 << 3,
    Dsr = 1 << 4,
    Ri = 1 << 5,

    /// <summary>Mask covering all output lines (DTR, RTS).</summary>
    OutputMask = Dtr | Rts,

    /// <summary>Mask covering all input lines (DCD, CTS, DSR, RI).</summary>
    InputMask = Dcd | Cts | Dsr | Ri,
}
