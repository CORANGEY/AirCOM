using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Connection handshake. Payload (8 bytes):
/// version[2] (big-endian) | capabilities[4] (bitmask) | options[2]
/// </summary>
public sealed class HandshakeMessage : IMessage
{
    public FrameType Type => FrameType.Handshake;

    /// <summary>Negotiated protocol version.</summary>
    public ushort Version { get; private set; }

    /// <summary>Capability bitmask.</summary>
    public HandshakeCapabilities Capabilities { get; private set; }

    /// <summary>Optional negotiation flags.</summary>
    public ushort Options { get; private set; }

    public HandshakeMessage() { }

    public HandshakeMessage(ushort version, HandshakeCapabilities capabilities, ushort options = 0)
    {
        Version = version;
        Capabilities = capabilities;
        Options = options;
    }

    public byte[] ToPayload()
    {
        var buf = new byte[8];
        PayloadWriter.WriteUInt16(buf, 0, Version);
        PayloadWriter.WriteUInt32(buf, 2, (uint)Capabilities);
        PayloadWriter.WriteUInt16(buf, 6, Options);
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
            throw new ArgumentException($"HANDSHAKE payload must be 8 bytes, got {payload.Length}.", nameof(payload));
        Version = PayloadReader.ReadUInt16(payload, 0);
        Capabilities = (HandshakeCapabilities)PayloadReader.ReadUInt32(payload, 2);
        Options = PayloadReader.ReadUInt16(payload, 6);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new HandshakeMessage();
        msg.FromPayload(payload);
        return msg;
    }
}

/// <summary>Capability bits advertised in the handshake.</summary>
[Flags]
public enum HandshakeCapabilities : uint
{
    None = 0,
    Encryption = 1 << 0,
    ControlLines = 1 << 1,
    Break = 1 << 2,
    Heartbeat = 1 << 3,
}
