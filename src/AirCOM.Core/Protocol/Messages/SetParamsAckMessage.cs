using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Parameter-application confirmation. Payload (3 bytes):
/// result[1] (0 = ok, non-zero = error) | errorCode[2] (big-endian)
/// </summary>
public sealed class SetParamsAckMessage : IMessage
{
    public FrameType Type => FrameType.SetParamsAck;

    public byte Result { get; private set; }
    public ushort ErrorCode { get; private set; }

    public bool IsOk => Result == 0;

    public SetParamsAckMessage() { }

    public SetParamsAckMessage(bool ok, ushort errorCode = 0) { Result = (byte)(ok ? 0 : 1); ErrorCode = errorCode; }

    public byte[] ToPayload()
    {
        var buf = new byte[3];
        buf[0] = Result;
        PayloadWriter.WriteUInt16(buf, 1, ErrorCode);
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3)
            throw new ArgumentException($"SET_PARAMS_ACK payload must be 3 bytes, got {payload.Length}.", nameof(payload));
        Result = payload[0];
        ErrorCode = PayloadReader.ReadUInt16(payload, 1);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new SetParamsAckMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
