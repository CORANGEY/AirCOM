using System.Net;
using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol.Messages;

/// <summary>
/// P2P hole-punch signaling carrier. Carries a list of ICE-like candidate addresses
/// to be exchanged via the signaling server. Payload layout:
/// count[1] | (candidateType[1] | addressFamily[1] | ip[4-or-16] | port[2]) repeated
/// Address family 0x01 = IPv4 (4 bytes), 0x02 = IPv6 (16 bytes). Port is big-endian.
/// </summary>
public sealed class P2pSignalingMessage : IMessage
{
    public FrameType Type => FrameType.P2pSignaling;

    public enum CandidateType : byte
    {
        Host = 0,
        ServerReflexive = 1,
        Relay = 2,
    }

    public sealed record Candidate(CandidateType Type, IPEndPoint EndPoint);

    public List<Candidate> Candidates { get; } = new();

    public P2pSignalingMessage() { }

    public P2pSignalingMessage(IEnumerable<Candidate> candidates)
    {
        Candidates.AddRange(candidates);
    }

    public byte[] ToPayload()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)Math.Min(Candidates.Count, 255));
        foreach (var c in Candidates)
        {
            w.Write((byte)c.Type);
            WriteEndPoint(w, c.EndPoint);
        }
        return ms.ToArray();
    }

    public void FromPayload(ReadOnlySpan<byte> payload)
    {
        Candidates.Clear();
        if (payload.Length < 1) return;
        int count = payload[0];
        int offset = 1;
        for (int i = 0; i < count; i++)
        {
            if (offset >= payload.Length) break;
            var type = (CandidateType)payload[offset++];
            var ep = ReadEndPoint(payload, ref offset);
            if (ep != null) Candidates.Add(new Candidate(type, ep));
        }
    }

    private static void WriteEndPoint(BinaryWriter w, IPEndPoint ep)
    {
        var addrBytes = ep.Address.GetAddressBytes();
        if (ep.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            w.Write((byte)0x01);
            w.Write(addrBytes); // 4 bytes
        }
        else
        {
            w.Write((byte)0x02);
            w.Write(addrBytes); // 16 bytes
        }
        w.Write((byte)((ep.Port >> 8) & 0xFF));
        w.Write((byte)(ep.Port & 0xFF));
    }

    private static IPEndPoint? ReadEndPoint(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset >= payload.Length) return null;
        byte family = payload[offset++];
        int addrLen = family == 0x01 ? 4 : 16;
        if (offset + addrLen + 2 > payload.Length) return null;
        var addrBytes = payload.Slice(offset, addrLen).ToArray();
        offset += addrLen;
        int port = (payload[offset] << 8) | payload[offset + 1];
        offset += 2;
        var addr = new IPAddress(addrBytes);
        return new IPEndPoint(addr, port);
    }

    public static IMessage Decode(ReadOnlySpan<byte> payload)
    {
        var msg = new P2pSignalingMessage();
        msg.FromPayload(payload);
        return msg;
    }
}
