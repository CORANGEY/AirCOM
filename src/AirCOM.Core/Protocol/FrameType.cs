namespace AirCOM.Core.Protocol;

/// <summary>
/// Wire-protocol message types. Values are stable and must never be reused.
/// </summary>
public enum FrameType : byte
{
    /// <summary>Serial data stream payload (most common).</summary>
    Data = 0x01,

    /// <summary>Serial port parameters (baud/data/stop/parity/flow).</summary>
    SetParams = 0x02,

    /// <summary>Control line status (DTR/RTS/DCD/CTS/DSR/RI bitmask).</summary>
    LineState = 0x03,

    /// <summary>Connection negotiation on establishment.</summary>
    Handshake = 0x04,

    /// <summary>Authentication (pairing code or session token).</summary>
    Auth = 0x05,

    /// <summary>Keepalive + loss detection.</summary>
    Heartbeat = 0x06,

    /// <summary>P2P hole-punch signaling carrier (candidate exchange).</summary>
    P2pSignaling = 0x07,

    /// <summary>Relay path establishment.</summary>
    RelayConnect = 0x08,

    /// <summary>Error notification.</summary>
    Error = 0x09,

    /// <summary>Reliable-transport acknowledgment.</summary>
    Ack = 0x0A,

    /// <summary>Serial break signal.</summary>
    Break = 0x0B,

    /// <summary>Parameter-application confirmation.</summary>
    SetParamsAck = 0x0C,
}
