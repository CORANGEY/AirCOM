using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// Authentication. Payload layout:
/// mode[1] (0 = pairing code, 1 = session token) | length[1] | credential[variable] | sessionId[4]
/// For pairing code: 6 ASCII digits. For token: 32 bytes.
/// </summary>
public sealed class AuthMessage : IMessage
{
    public FrameType Type => FrameType.Auth;

    public enum AuthMode : byte
    {
        PairingCode = 0,
        SessionToken = 1,
    }

    public AuthMode Mode { get; private set; }
    public byte[] Credential { get; private set; } = Array.Empty<byte>();
    public uint SessionId { get; private set; }

    public AuthMessage() { }

    public AuthMessage(AuthMode mode, byte[] credential, uint sessionId)
    {
        Mode = mode;
        Credential = credential ?? throw new ArgumentNullException(nameof(credential));
        SessionId = sessionId;
    }

    /// <summary>Construct from a 6-digit pairing code string.</summary>
    public static AuthMessage FromPairingCode(string pairingCode, uint sessionId)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(pairingCode);
        return new AuthMessage(AuthMode.PairingCode, bytes, sessionId);
    }

    public byte[] ToPayload()
    {
        var buf = new byte[2 + Credential.Length + 4];
        buf[0] = (byte)Mode;
        buf[1] = (byte)Credential.Length;
        Credential.AsSpan().CopyTo(buf.AsSpan(2));
        PayloadWriter.WriteUInt32(buf, 2 + Credential.Length, SessionId);
        return buf;
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
            throw new ArgumentException($"AUTH payload too short: {payload.Length}.", nameof(payload));
        Mode = (AuthMode)payload[0];
        int len = payload[1];
        if (payload.Length < 2 + len + 4)
            throw new ArgumentException($"AUTH payload truncated.", nameof(payload));
        Credential = payload.Slice(2, len).ToArray();
        SessionId = PayloadReader.ReadUInt32(payload, 2 + len);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new AuthMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
