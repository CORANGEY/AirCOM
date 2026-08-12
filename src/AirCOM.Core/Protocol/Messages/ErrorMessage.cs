using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Error notification. Payload:
/// errorCode[2] (big-endian) | messageLength[1] | message[variable ASCII]
/// </summary>
public sealed class ErrorMessage : IMessage
{
    public FrameType Type => FrameType.Error;

    public ushort ErrorCode { get; private set; }
    public string Message { get; private set; } = string.Empty;

    public ErrorMessage() { }

    public ErrorMessage(ushort errorCode, string message)
    {
        ErrorCode = errorCode;
        Message = message ?? string.Empty;
    }

    public byte[] ToPayload()
    {
        var msgBytes = System.Text.Encoding.ASCII.GetBytes(Message);
        if (msgBytes.Length > 255)
            msgBytes = msgBytes.AsSpan(0, 255).ToArray();
        var buf = new byte[3 + msgBytes.Length];
        PayloadWriter.WriteUInt16(buf, 0, ErrorCode);
        buf[2] = (byte)msgBytes.Length;
        msgBytes.AsSpan().CopyTo(buf.AsSpan(3));
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3)
            throw new ArgumentException($"ERROR payload too short: {payload.Length}.", nameof(payload));
        ErrorCode = PayloadReader.ReadUInt16(payload, 0);
        int len = payload[2];
        Message = len == 0 ? string.Empty
            : System.Text.Encoding.ASCII.GetString(payload.Slice(3, Math.Min(len, payload.Length - 3)));
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new ErrorMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
