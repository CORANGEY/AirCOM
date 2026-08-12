using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Heartbeat / keepalive. Payload (12 bytes):
/// timestamp[8] (big-endian, milliseconds since epoch) | lastSequence[4]
/// </summary>
public sealed class HeartbeatMessage : IMessage
{
    public FrameType Type => FrameType.Heartbeat;

    public long TimestampMs { get; private set; }
    public uint LastSequence { get; private set; }

    public HeartbeatMessage() { }

    public HeartbeatMessage(long timestampMs, uint lastSequence)
    {
        TimestampMs = timestampMs;
        LastSequence = lastSequence;
    }

    public byte[] ToPayload()
    {
        var buf = new byte[12];
        // Big-endian 64-bit timestamp
        ulong ts = (ulong)TimestampMs;
        buf[0] = (byte)(ts >> 56);
        buf[1] = (byte)(ts >> 48);
        buf[2] = (byte)(ts >> 40);
        buf[3] = (byte)(ts >> 32);
        buf[4] = (byte)(ts >> 24);
        buf[5] = (byte)(ts >> 16);
        buf[6] = (byte)(ts >> 8);
        buf[7] = (byte)ts;
        PayloadWriter.WriteUInt32(buf, 8, LastSequence);
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12)
            throw new ArgumentException($"HEARTBEAT payload must be 12 bytes, got {payload.Length}.", nameof(payload));
        ulong ts = ((ulong)payload[0] << 56) | ((ulong)payload[1] << 48) |
                   ((ulong)payload[2] << 40) | ((ulong)payload[3] << 32) |
                   ((ulong)payload[4] << 24) | ((ulong)payload[5] << 16) |
                   ((ulong)payload[6] << 8) | payload[7];
        TimestampMs = (long)ts;
        LastSequence = PayloadReader.ReadUInt32(payload, 8);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new HeartbeatMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
