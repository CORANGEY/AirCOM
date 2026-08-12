using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Serial;
using FluentAssertions;
using Xunit;

namespace AirCOM.Core.Tests.Protocol;

/// <summary>
/// Round-trip tests for each message type: ToPayload -> FromPayload recovers all fields.
/// </summary>
public class MessageCodecTests
{
    [Fact]
    public void DataMessage_RoundTrip()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        var msg = new DataMessage(data);

        byte[] payload = msg.ToPayload();
        var decoded = (DataMessage)MessageCodec.Decode(FrameType.Data, payload)!;

        decoded.Data.Should().Equal(data);
    }

    [Fact]
    public void SetParamsMessage_RoundTrip()
    {
        var msg = new SetParamsMessage(new SerialParams(9600, 7, StopBitsKind.Two, Parity.Even, FlowControl.Hardware));

        byte[] payload = msg.ToPayload();
        var decoded = (SetParamsMessage)MessageCodec.Decode(FrameType.SetParams, payload)!;

        decoded.Params.BaudRate.Should().Be(9600);
        decoded.Params.DataBits.Should().Be(7);
        decoded.Params.StopBits.Should().Be(StopBitsKind.Two);
        decoded.Params.Parity.Should().Be(Parity.Even);
        decoded.Params.FlowControl.Should().Be(FlowControl.Hardware);
    }

    [Fact]
    public void SetParamsMessage_UnknownBaudRate_EncodesSentinel()
    {
        var msg = new SetParamsMessage(new SerialParams(SerialParams.UnknownBaudRate, 8,
            StopBitsKind.One, Parity.None, FlowControl.None));

        byte[] payload = msg.ToPayload();
        var decoded = (SetParamsMessage)MessageCodec.Decode(FrameType.SetParams, payload)!;

        decoded.Params.BaudRate.Should().Be(SerialParams.UnknownBaudRate);
        decoded.Params.HasUnknown.Should().BeTrue();
    }

    [Fact]
    public void LineStateMessage_RoundTrip()
    {
        var msg = new LineStateMessage(
            output: ModemStatusFlags.Dtr | ModemStatusFlags.Rts,
            input: ModemStatusFlags.Dcd | ModemStatusFlags.Cts | ModemStatusFlags.Ri,
            changeMask: ModemStatusFlags.Dtr | ModemStatusFlags.Dcd);

        byte[] payload = msg.ToPayload();
        var decoded = (LineStateMessage)MessageCodec.Decode(FrameType.LineState, payload)!;

        decoded.OutputLines.Should().HaveFlag(ModemStatusFlags.Dtr);
        decoded.OutputLines.Should().HaveFlag(ModemStatusFlags.Rts);
        decoded.InputLines.Should().HaveFlag(ModemStatusFlags.Dcd);
        decoded.InputLines.Should().HaveFlag(ModemStatusFlags.Cts);
        decoded.InputLines.Should().HaveFlag(ModemStatusFlags.Ri);
        decoded.InputLines.Should().NotHaveFlag(ModemStatusFlags.Dsr);
        decoded.ChangeMask.Should().HaveFlag(ModemStatusFlags.Dtr);
        decoded.ChangeMask.Should().HaveFlag(ModemStatusFlags.Dcd);
    }

    [Fact]
    public void HandshakeMessage_RoundTrip()
    {
        var msg = new HandshakeMessage(0x0001, HandshakeCapabilities.Encryption | HandshakeCapabilities.ControlLines, 0x0042);

        byte[] payload = msg.ToPayload();
        var decoded = (HandshakeMessage)MessageCodec.Decode(FrameType.Handshake, payload)!;

        decoded.Version.Should().Be(0x0001);
        decoded.Capabilities.Should().Be(HandshakeCapabilities.Encryption | HandshakeCapabilities.ControlLines);
        decoded.Options.Should().Be(0x0042);
    }

    [Fact]
    public void AuthMessage_PairingCode_RoundTrip()
    {
        var msg = AuthMessage.FromPairingCode("123456", sessionId: 0xCAFEBABE);

        byte[] payload = msg.ToPayload();
        var decoded = (AuthMessage)MessageCodec.Decode(FrameType.Auth, payload)!;

        decoded.Mode.Should().Be(AuthMessage.AuthMode.PairingCode);
        decoded.Credential.Should().Equal(System.Text.Encoding.ASCII.GetBytes("123456"));
        decoded.SessionId.Should().Be(0xCAFEBABE);
    }

    [Fact]
    public void AuthMessage_SessionToken_RoundTrip()
    {
        var token = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var msg = new AuthMessage(AuthMessage.AuthMode.SessionToken, token, 1);

        byte[] payload = msg.ToPayload();
        var decoded = (AuthMessage)MessageCodec.Decode(FrameType.Auth, payload)!;

        decoded.Mode.Should().Be(AuthMessage.AuthMode.SessionToken);
        decoded.Credential.Should().Equal(token);
    }

    [Fact]
    public void HeartbeatMessage_RoundTrip()
    {
        var msg = new HeartbeatMessage(timestampMs: 0x1234567890ABCDEF, lastSequence: 42);

        byte[] payload = msg.ToPayload();
        var decoded = (HeartbeatMessage)MessageCodec.Decode(FrameType.Heartbeat, payload)!;

        decoded.TimestampMs.Should().Be(0x1234567890ABCDEF);
        decoded.LastSequence.Should().Be(42);
    }

    [Fact]
    public void P2pSignalingMessage_RoundTrip_WithCandidates()
    {
        var candidates = new[]
        {
            new P2pSignalingMessage.Candidate(
                P2pSignalingMessage.CandidateType.Host,
                new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.100"), 50000)),
            new P2pSignalingMessage.Candidate(
                P2pSignalingMessage.CandidateType.ServerReflexive,
                new System.Net.IPEndPoint(System.Net.IPAddress.Parse("203.0.113.5"), 51234)),
        };
        var msg = new P2pSignalingMessage(candidates);

        byte[] payload = msg.ToPayload();
        var decoded = (P2pSignalingMessage)MessageCodec.Decode(FrameType.P2pSignaling, payload)!;

        decoded.Candidates.Should().HaveCount(2);
        decoded.Candidates[0].Type.Should().Be(P2pSignalingMessage.CandidateType.Host);
        decoded.Candidates[0].EndPoint.Address.ToString().Should().Be("192.168.1.100");
        decoded.Candidates[0].EndPoint.Port.Should().Be(50000);
        decoded.Candidates[1].Type.Should().Be(P2pSignalingMessage.CandidateType.ServerReflexive);
        decoded.Candidates[1].EndPoint.Address.ToString().Should().Be("203.0.113.5");
        decoded.Candidates[1].EndPoint.Port.Should().Be(51234);
    }

    [Fact]
    public void RelayConnectMessage_RoundTrip()
    {
        var token = Enumerable.Range(0, 32).Select(i => (byte)(i * 2)).ToArray();
        var msg = new RelayConnectMessage(token, RelayConnectMessage.RelayDirection.BSide);

        byte[] payload = msg.ToPayload();
        var decoded = (RelayConnectMessage)MessageCodec.Decode(FrameType.RelayConnect, payload)!;

        decoded.SessionToken.Should().Equal(token);
        decoded.Direction.Should().Be(RelayConnectMessage.RelayDirection.BSide);
    }

    [Fact]
    public void ErrorMessage_RoundTrip()
    {
        var msg = new ErrorMessage(0x1001, "Serial port open failed");

        byte[] payload = msg.ToPayload();
        var decoded = (ErrorMessage)MessageCodec.Decode(FrameType.Error, payload)!;

        decoded.ErrorCode.Should().Be(0x1001);
        decoded.Message.Should().Be("Serial port open failed");
    }

    [Fact]
    public void AckMessage_RoundTrip()
    {
        var msg = new AckMessage(ackedSequence: 99);

        byte[] payload = msg.ToPayload();
        var decoded = (AckMessage)MessageCodec.Decode(FrameType.Ack, payload)!;

        decoded.AckedSequence.Should().Be(99);
    }

    [Fact]
    public void BreakMessage_RoundTrip()
    {
        var msg = new BreakMessage(durationMs: 250);

        byte[] payload = msg.ToPayload();
        var decoded = (BreakMessage)MessageCodec.Decode(FrameType.Break, payload)!;

        decoded.DurationMs.Should().Be(250);
    }

    [Fact]
    public void SetParamsAckMessage_RoundTrip_Ok()
    {
        var msg = new SetParamsAckMessage(ok: true);

        byte[] payload = msg.ToPayload();
        var decoded = (SetParamsAckMessage)MessageCodec.Decode(FrameType.SetParamsAck, payload)!;

        decoded.IsOk.Should().BeTrue();
    }

    [Fact]
    public void SetParamsAckMessage_RoundTrip_Error()
    {
        var msg = new SetParamsAckMessage(ok: false, errorCode: 0x2002);

        byte[] payload = msg.ToPayload();
        var decoded = (SetParamsAckMessage)MessageCodec.Decode(FrameType.SetParamsAck, payload)!;

        decoded.IsOk.Should().BeFalse();
        decoded.ErrorCode.Should().Be(0x2002);
    }

    [Fact]
    public void Decode_UnknownType_ReturnsNull()
    {
        var decoded = MessageCodec.Decode((FrameType)0xFF, new byte[] { 1, 2 });

        decoded.Should().BeNull();
    }
}
