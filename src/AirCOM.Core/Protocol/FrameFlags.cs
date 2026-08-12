namespace AirCOM.Core.Protocol;

/// <summary>
/// Control flags carried in the 1-byte Flags field of every frame header.
/// </summary>
[Flags]
public enum FrameFlags : byte
{
    None = 0,

    /// <summary>Payload is encrypted (AEAD).</summary>
    Encrypted = 1 << 0,

    /// <summary>Payload is compressed (reserved for future use).</summary>
    Compressed = 1 << 1,

    /// <summary>Sender requests an ACK for this frame's sequence number.</summary>
    AckRequest = 1 << 2,

    /// <summary>This frame is itself an acknowledgment (carries the acked seq in payload).</summary>
    AckResponse = 1 << 3,
}
