namespace AirCOM.Core.Protocol;

/// <summary>
/// Fixed-size frame header (15 bytes) preceding every frame's payload and CRC.
/// All multi-byte fields are big-endian on the wire.
/// </summary>
public readonly record struct FrameHeader(
    byte Version,
    FrameType Type,
    uint SessionId,
    uint Sequence,
    ushort PayloadLength,
    FrameFlags Flags)
{
    /// <summary>Frame start marker. Chosen to be unlikely to appear in serial data.</summary>
    public const ushort Magic = 0xA5C3;

    /// <summary>Current protocol version.</summary>
    public const byte CurrentVersion = 0x01;

    /// <summary>Fixed header size in bytes (excludes payload and trailing CRC16).</summary>
    public const int HeaderSize = 15;

    /// <summary>Trailing CRC16 size in bytes.</summary>
    public const int CrcSize = 2;

    /// <summary>Total overhead per frame: header + CRC.</summary>
    public const int OverheadSize = HeaderSize + CrcSize;

    /// <summary>Maximum payload length (limited by the 2-byte length field).</summary>
    public const ushort MaxPayloadLength = ushort.MaxValue;

    /// <summary>Total on-wire size of a frame with the given payload length.</summary>
    public static int WireSize(int payloadLength) => HeaderSize + payloadLength + CrcSize;

    /// <summary>True when the payload should be treated as encrypted ciphertext.</summary>
    public bool IsEncrypted => (Flags & FrameFlags.Encrypted) != 0;
}
