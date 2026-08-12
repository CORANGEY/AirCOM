using AirCOM.Core.Util;

namespace AirCOM.Core.Protocol;

/// <summary>
/// Encodes <see cref="Frame"/> objects to and from the on-wire binary representation.
/// Wire layout: Magic(2) | Version(1) | Type(1) | SessionId(4) | Sequence(4) |
///              PayloadLength(2) | Flags(1) | Payload(0..65535) | Crc16(2)
/// All multi-byte header fields are big-endian.
/// </summary>
public static class FrameCodec
{
    /// <summary>Encodes a frame into a fresh byte array.</summary>
    public static byte[] Encode(Frame frame)
    {
        var header = frame.Header;
        int wireSize = FrameHeader.WireSize(header.PayloadLength);
        var buffer = new byte[wireSize];
        Encode(frame, buffer);
        return buffer;
    }

    /// <summary>Encodes a frame into the provided buffer. Returns bytes written.</summary>
    public static int Encode(Frame frame, Span<byte> destination)
    {
        var header = frame.Header;
        int wireSize = FrameHeader.WireSize(header.PayloadLength);

        if (destination.Length < wireSize)
            throw new ArgumentException(
                $"Destination buffer too small: need {wireSize}, have {destination.Length}.", nameof(destination));

        if (header.PayloadLength != (frame.Payload?.Length ?? 0))
            throw new ArgumentException(
                $"Payload length mismatch: header says {header.PayloadLength}, payload has {frame.Payload?.Length ?? 0}.",
                nameof(frame));

        // Header
        WriteUInt16Big(destination, 0, FrameHeader.Magic);
        destination[2] = header.Version;
        destination[3] = (byte)header.Type;
        WriteUInt32Big(destination, 4, header.SessionId);
        WriteUInt32Big(destination, 8, header.Sequence);
        WriteUInt16Big(destination, 12, header.PayloadLength);
        destination[14] = (byte)header.Flags;

        // Payload
        int payloadStart = FrameHeader.HeaderSize;
        if (header.PayloadLength > 0)
        {
            frame.Payload.AsSpan(0, header.PayloadLength).CopyTo(destination.Slice(payloadStart, header.PayloadLength));
        }

        // CRC16 over header + payload
        int crcLength = FrameHeader.HeaderSize + header.PayloadLength;
        ushort crc = Crc16.Compute(destination.Slice(0, crcLength));
        WriteUInt16Big(destination, crcLength, crc);

        return wireSize;
    }

    /// <summary>
    /// Attempts to decode a single frame from the front of <paramref name="source"/>.
    /// On success returns the frame and advances past it via <paramref name="bytesConsumed"/>.
    /// Returns null if <paramref name="source"/> does not contain a complete, valid frame.
    /// If a magic mismatch is found at the start, the caller should resync by skipping bytes.
    /// </summary>
    public static Frame? TryDecode(ReadOnlySpan<byte> source, out int bytesConsumed)
    {
        bytesConsumed = 0;

        // Need at least a header to inspect.
        if (source.Length < FrameHeader.HeaderSize)
            return null;

        // Resync: if magic not at position 0, signal the caller to skip forward.
        if (ReadUInt16Big(source, 0) != FrameHeader.Magic)
            return null;

        var payloadLength = ReadUInt16Big(source, 12);
        int wireSize = FrameHeader.WireSize(payloadLength);

        // Wait until the full frame (header + payload + CRC) is available.
        if (source.Length < wireSize)
            return null;

        var header = new FrameHeader(
            Version: source[2],
            Type: (FrameType)source[3],
            SessionId: ReadUInt32Big(source, 4),
            Sequence: ReadUInt32Big(source, 8),
            PayloadLength: payloadLength,
            Flags: (FrameFlags)source[14]);

        if (header.Version != FrameHeader.CurrentVersion)
            return null;

        // Verify CRC16 over header + payload.
        int crcLength = FrameHeader.HeaderSize + payloadLength;
        ushort expectedCrc = ReadUInt16Big(source, crcLength);
        ushort actualCrc = Crc16.Compute(source.Slice(0, crcLength));
        if (actualCrc != expectedCrc)
            return null;

        // Copy payload out so the caller owns it.
        byte[] payload = payloadLength == 0
            ? Array.Empty<byte>()
            : source.Slice(FrameHeader.HeaderSize, payloadLength).ToArray();

        bytesConsumed = wireSize;
        return new Frame(header, payload);
    }

    internal static void WriteUInt16Big(Span<byte> buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)value;
    }

    internal static void WriteUInt32Big(Span<byte> buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    internal static ushort ReadUInt16Big(ReadOnlySpan<byte> buf, int offset) =>
        (ushort)((buf[offset] << 8) | buf[offset + 1]);

    internal static uint ReadUInt32Big(ReadOnlySpan<byte> buf, int offset) =>
        ((uint)buf[offset] << 24) | ((uint)buf[offset + 1] << 16) |
        ((uint)buf[offset + 2] << 8) | buf[offset + 3];
}
