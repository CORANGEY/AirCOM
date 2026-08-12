namespace AirCOM.Core.Protocol;

/// <summary>Parity options, encoded per RFC 2217 semantics.</summary>
public enum Parity : byte
{
    Unknown = 0,
    None = 1,
    Odd = 2,
    Even = 3,
    Mark = 4,
    Space = 5,
}

/// <summary>Stop bits, encoded per RFC 2217 semantics.</summary>
public enum StopBitsKind : byte
{
    Unknown = 0,
    One = 1,
    OnePointFive = 2,
    Two = 3,
}

/// <summary>Flow control options.</summary>
public enum FlowControl : byte
{
    Unknown = 0,
    None = 1,
    Hardware = 2,
    Software = 3,
    Custom = 4,
}

/// <summary>
/// Complete serial port parameter set. Serialized into the SET_PARAMS payload
/// (8 bytes: baud[4] + dataBits[1] + stopBits[1] + parity[1] + flowControl[1]).
/// Field encoding aligns with RFC 2217 Com Port Option semantics.
/// </summary>
public readonly record struct SerialParams(
    uint BaudRate,
    byte DataBits,
    StopBitsKind StopBits,
    Parity Parity,
    FlowControl FlowControl)
{
    /// <summary>0xFFFFFFFF as defined by RFC 2217 for "unknown baud rate".</summary>
    public const uint UnknownBaudRate = 0xFFFFFFFF;

    public static SerialParams Default => new(115200, 8, StopBitsKind.One, Parity.None, FlowControl.None);

    /// <summary>True when any field is in its Unknown state.</summary>
    public bool HasUnknown =>
        BaudRate == UnknownBaudRate ||
        DataBits == 0 ||
        StopBits == StopBitsKind.Unknown ||
        Parity == Parity.Unknown ||
        FlowControl == FlowControl.Unknown;
}
