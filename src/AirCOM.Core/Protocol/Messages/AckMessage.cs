using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Reliable-transport acknowledgment. Payload (4 bytes): ackedSequence[4] (big-endian).
/// </summary>
public sealed class AckMessage : IMessage
{
    public FrameType Type => FrameType.Ack;

    public uint AckedSequence { get; private set; }

    public AckMessage() { }

    public AckMessage(uint ackedSequence) { AckedSequence = ackedSequence; }

    public byte[] ToPayload()
    {
        var buf = new byte[4];
        PayloadWriter.WriteUInt32(buf, 0, AckedSequence);
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
            throw new ArgumentException($"ACK payload must be 4 bytes, got {payload.Length}.", nameof(payload));
        AckedSequence = PayloadReader.ReadUInt32(payload, 0);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new AckMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
