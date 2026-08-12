namespace AirCOM.Core.Protocol;

/// <summary>
/// A decoded protocol frame: its header plus a payload buffer the caller owns.
/// </summary>
public sealed class Frame
{
    public FrameHeader Header { get; init; }

    /// <summary>Payload bytes. For <see cref="FrameType.Data"/> this is raw serial bytes.</summary>
    public byte[] Payload { get; init; }

    public Frame(FrameHeader header, byte[] payload)
    {
        Header = header;
        Payload = payload;
    }

    public FrameType Type => Header.Type;
    public uint Sequence => Header.Sequence;
    public uint SessionId => Header.SessionId;
    public FrameFlags Flags => Header.Flags;

    /// <summary>Creates a new DATA frame. The caller supplies the sequence number.</summary>
    public static Frame Data(byte[] payload, uint sessionId, uint sequence, FrameFlags flags = FrameFlags.None) =>
        new(new FrameHeader(FrameHeader.CurrentVersion, FrameType.Data, sessionId, sequence,
            (ushort)payload.Length, flags), payload);
}
