using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Serial break signal. Payload (2 bytes): durationMs[2] (big-endian).
/// </summary>
public sealed class BreakMessage : IMessage
{
    public FrameType Type => FrameType.Break;

    public ushort DurationMs { get; private set; }

    public BreakMessage() { }

    public BreakMessage(ushort durationMs) { DurationMs = durationMs; }

    public byte[] ToPayload()
    {
        var buf = new byte[2];
        PayloadWriter.WriteUInt16(buf, 0, DurationMs);
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
            throw new ArgumentException($"BREAK payload must be 2 bytes, got {payload.Length}.", nameof(payload));
        DurationMs = PayloadReader.ReadUInt16(payload, 0);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new BreakMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
