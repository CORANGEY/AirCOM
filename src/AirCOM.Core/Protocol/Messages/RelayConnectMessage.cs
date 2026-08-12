using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Relay path establishment. Payload (33 bytes):
/// sessionToken[32] | direction[1] (0 = A-side connecting, 1 = B-side connecting)
/// </summary>
public sealed class RelayConnectMessage : IMessage
{
    public FrameType Type => FrameType.RelayConnect;

    public enum RelayDirection : byte
    {
        /// <summary>The A-side (consumer) is connecting to the relay.</summary>
        ASide = 0,
        /// <summary>The B-side (provider) is connecting to the relay.</summary>
        BSide = 1,
    }

    public byte[] SessionToken { get; private set; } = new byte[32];
    public RelayDirection Direction { get; private set; }

    public RelayConnectMessage() { }

    public RelayConnectMessage(byte[] sessionToken, RelayDirection direction)
    {
        if (sessionToken == null || sessionToken.Length != 32)
            throw new ArgumentException("Session token must be 32 bytes.", nameof(sessionToken));
        SessionToken = sessionToken;
        Direction = direction;
    }

    public byte[] ToPayload()
    {
        var buf = new byte[33];
        SessionToken.AsSpan().CopyTo(buf.AsSpan(0, 32));
        buf[32] = (byte)Direction;
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 33)
            throw new ArgumentException($"RELAY_CONNECT payload must be 33 bytes, got {payload.Length}.", nameof(payload));
        SessionToken = payload.Slice(0, 32).ToArray();
        Direction = (RelayDirection)payload[32];
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new RelayConnectMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
