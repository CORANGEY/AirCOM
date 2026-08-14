using AirCOM.Core.Serial;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// A typed protocol message that knows how to serialize itself into a frame payload
/// and deserialize from one. Each <see cref="FrameType"/> maps to one message type.
/// </summary>
public interface IMessage
{
    FrameType Type { get; }

    /// <summary>Serialize the message to a payload byte array.</summary>
    byte[] ToPayload();

    /// <summary>Deserialize a payload into this message instance.</summary>
    void FromPayload(ReadOnlySpan<byte> payload);
}

/// <summary>
/// Dispatches payload deserialization based on frame type.
/// </summary>
public static class MessageCodec
{
    /// <summary>
    /// Decodes a frame's payload into a typed message, or null if the type is unknown.
    /// </summary>
    public static IMessage? Decode(Frame frame) => Decode(frame.Type, frame.Payload);

    public static IMessage? Decode(FrameType type, ReadOnlySpan<byte> payload) => type switch
    {
        FrameType.Data => DataMessage.Decode(payload),
        FrameType.SetParams => SetParamsMessage.Decode(payload),
        FrameType.LineState => LineStateMessage.Decode(payload),
        FrameType.Handshake => HandshakeMessage.Decode(payload),
        FrameType.Auth => AuthMessage.Decode(payload),
        FrameType.Heartbeat => HeartbeatMessage.Decode(payload),
        FrameType.P2pSignaling => P2pSignalingMessage.Decode(payload),
        FrameType.RelayConnect => RelayConnectMessage.Decode(payload),
        FrameType.Error => ErrorMessage.Decode(payload),
        FrameType.Ack => AckMessage.Decode(payload),
        FrameType.Break => BreakMessage.Decode(payload),
        FrameType.SetParamsAck => SetParamsAckMessage.Decode(payload),
        FrameType.Matched => MatchedMessage.Decode(payload),
        FrameType.SignalingError => SignalingErrorMessage.Decode(payload),
        _ => null,
    };
}

/// <summary>Shared big-endian read helpers for message payloads.</summary>
internal static class PayloadReader
{
    public static ushort ReadUInt16(ReadOnlySpan<byte> b, int o) => (ushort)((b[o] << 8) | b[o + 1]);
    public static uint ReadUInt32(ReadOnlySpan<byte> b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
}

/// <summary>Shared big-endian write helpers for message payloads.</summary>
internal static class PayloadWriter
{
    public static void WriteUInt16(Span<byte> b, int o, ushort v)
    {
        b[o] = (byte)(v >> 8);
        b[o + 1] = (byte)v;
    }
    public static void WriteUInt32(Span<byte> b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }
}
