using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Raw serial data stream. Payload is the literal serial bytes with no wrapping.
/// </summary>
public sealed class DataMessage : IMessage
{
    public FrameType Type => FrameType.Data;

    /// <summary>The serial data bytes.</summary>
    public byte[] Data { get; private set; } = Array.Empty<byte>();

    public DataMessage() { }

    public DataMessage(byte[] data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public byte[] ToPayload() => Data;

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        Data = payload.IsEmpty ? Array.Empty<byte>() : payload.ToArray();
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new DataMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
