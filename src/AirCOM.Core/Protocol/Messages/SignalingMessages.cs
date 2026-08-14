using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Server -> both peers: pairing succeeded (M2 signaling).
/// Payload: sessionId[4] (big-endian). Both peers then switch their connection
/// into relay-pipe mode (raw bidirectional forwarding of the existing frames).
/// </summary>
public sealed class MatchedMessage : IMessage
{
    public FrameType Type => FrameType.Matched;

    public uint SessionId { get; private set; }

    public MatchedMessage() { }

    public MatchedMessage(uint sessionId) { SessionId = sessionId; }

    public byte[] ToPayload()
    {
        var buf = new byte[4];
        PayloadWriter.WriteUInt32(buf, 0, SessionId);
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
            throw new ArgumentException($"MATCHED payload must be 4 bytes, got {payload.Length}.", nameof(payload));
        SessionId = PayloadReader.ReadUInt32(payload, 0);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new MatchedMessage();
        msg.FromPayload(payload);
        return msg;
    }
}

/// <summary>
/// Server -> client: signaling error. Payload: errorCode[2] (big-endian) | message[variable ASCII].
/// </summary>
public sealed class SignalingErrorMessage : IMessage
{
    public FrameType Type => FrameType.SignalingError;

    // Error codes
    public const ushort CodeBadPairingCode = 1;
    public const ushort CodePairingTimeout = 2;
    public const ushort CodePeerDisconnected = 3;
    public const ushort CodeServerError = 9;

    public ushort ErrorCode { get; private set; }
    public string Message { get; private set; } = string.Empty;

    public SignalingErrorMessage() { }

    public SignalingErrorMessage(ushort errorCode, string message)
    {
        ErrorCode = errorCode;
        Message = message ?? string.Empty;
    }

    public byte[] ToPayload()
    {
        var msgBytes = System.Text.Encoding.ASCII.GetBytes(Message);
        var buf = new byte[2 + msgBytes.Length];
        PayloadWriter.WriteUInt16(buf, 0, ErrorCode);
        msgBytes.AsSpan().CopyTo(buf.AsSpan(2));
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
            throw new ArgumentException("SIGNALING_ERROR payload too short.", nameof(payload));
        ErrorCode = PayloadReader.ReadUInt16(payload, 0);
        Message = payload.Length > 2
            ? System.Text.Encoding.ASCII.GetString(payload[2..])
            : string.Empty;
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new SignalingErrorMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
