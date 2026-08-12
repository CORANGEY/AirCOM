using AirCOM.Core.Protocol;
using FluentAssertions;
using Xunit;

namespace AirCOM.Core.Tests.Protocol;

/// <summary>
/// Round-trip and edge-case tests for the frame codec: encode -> decode should
/// recover identical header fields and payload, with correct CRC and resync behavior.
/// </summary>
public class FrameCodecTests
{
    private const uint TestSessionId = 0x12345678;
    private const uint TestSequence = 0xABCDEF01;

    [Fact]
    public void EncodeDecodeRoundTrip_DataFrame_RecoversAllFields()
    {
        var payload = new byte[] { 0x41, 0x42, 0x43, 0x44 }; // "ABCD"
        var frame = Frame.Data(payload, TestSessionId, TestSequence, FrameFlags.None);

        byte[] encoded = FrameCodec.Encode(frame);
        var decoded = FrameCodec.TryDecode(encoded, out int consumed);

        decoded.Should().NotBeNull();
        consumed.Should().Be(encoded.Length);
        decoded!.Type.Should().Be(FrameType.Data);
        decoded.SessionId.Should().Be(TestSessionId);
        decoded.Sequence.Should().Be(TestSequence);
        decoded.Header.PayloadLength.Should().Be((ushort)payload.Length);
        decoded.Payload.Should().Equal(payload);
    }

    [Fact]
    public void Encode_WritesMagicAndBigEndianHeaderFields()
    {
        var frame = Frame.Data(new byte[] { 1, 2 }, TestSessionId, TestSequence, FrameFlags.Encrypted);
        byte[] encoded = FrameCodec.Encode(frame);

        // Magic at offset 0, big-endian.
        encoded[0].Should().Be(0xA5);
        encoded[1].Should().Be(0xC3);
        // Version at offset 2.
        encoded[2].Should().Be(FrameHeader.CurrentVersion);
        // Type at offset 3.
        encoded[3].Should().Be((byte)FrameType.Data);
        // SessionId big-endian at offset 4.
        encoded[4].Should().Be(0x12);
        encoded[5].Should().Be(0x34);
        encoded[6].Should().Be(0x56);
        encoded[7].Should().Be(0x78);
        // Sequence big-endian at offset 8.
        encoded[8].Should().Be(0xAB);
        encoded[9].Should().Be(0xCD);
        encoded[10].Should().Be(0xEF);
        encoded[11].Should().Be(0x01);
        // PayloadLength big-endian at offset 12.
        encoded[12].Should().Be(0x00);
        encoded[13].Should().Be(0x02);
        // Flags at offset 14.
        encoded[14].Should().Be((byte)FrameFlags.Encrypted);
    }

    [Fact]
    public void WireSize_HeaderAndCrcOverheadIs17BytesForEmptyPayload()
    {
        var frame = Frame.Data(Array.Empty<byte>(), TestSessionId, TestSequence);
        byte[] encoded = FrameCodec.Encode(frame);

        // 15-byte header + 0 payload + 2-byte CRC = 17 bytes total.
        encoded.Length.Should().Be(17);
    }

    [Fact]
    public void TryDecode_EmptyPayload_DecodesSuccessfully()
    {
        var frame = Frame.Data(Array.Empty<byte>(), TestSessionId, TestSequence);
        byte[] encoded = FrameCodec.Encode(frame);

        var decoded = FrameCodec.TryDecode(encoded, out int consumed);

        decoded.Should().NotBeNull();
        consumed.Should().Be(17);
        decoded!.Header.PayloadLength.Should().Be(0);
        decoded.Payload.Should().BeEmpty();
    }

    [Fact]
    public void TryDecode_LargePayload_DecodesSuccessfully()
    {
        var payload = Enumerable.Range(0, 10000).Select(i => (byte)(i & 0xFF)).ToArray();
        var frame = Frame.Data(payload, TestSessionId, TestSequence);
        byte[] encoded = FrameCodec.Encode(frame);

        var decoded = FrameCodec.TryDecode(encoded, out int consumed);

        decoded.Should().NotBeNull();
        consumed.Should().Be(encoded.Length);
        decoded!.Payload.Should().Equal(payload);
    }

    [Fact]
    public void TryDecode_TruncatedFrame_ReturnsNull()
    {
        var payload = new byte[100];
        var frame = Frame.Data(payload, TestSessionId, TestSequence);
        byte[] encoded = FrameCodec.Encode(frame);

        // Truncate the encoded frame.
        var truncated = encoded.AsSpan(0, 50).ToArray();

        var decoded = FrameCodec.TryDecode(truncated, out int consumed);

        decoded.Should().BeNull();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryDecode_CorruptedCrc_ReturnsNull()
    {
        var frame = Frame.Data(new byte[] { 1, 2, 3, 4 }, TestSessionId, TestSequence);
        byte[] encoded = FrameCodec.Encode(frame);

        // Corrupt the last byte (CRC16 low byte).
        encoded[^1] ^= 0xFF;

        var decoded = FrameCodec.TryDecode(encoded, out int consumed);

        decoded.Should().BeNull();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryDecode_MagicMismatch_ReturnsNull()
    {
        var frame = Frame.Data(new byte[] { 1 }, TestSessionId, TestSequence);
        byte[] encoded = FrameCodec.Encode(frame);

        // Corrupt the magic.
        encoded[0] = 0x00;

        var decoded = FrameCodec.TryDecode(encoded, out int consumed);

        decoded.Should().BeNull();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryDecode_PartialHeader_ReturnsNull()
    {
        // Only 5 bytes — not enough for the 15-byte header.
        var tooShort = new byte[] { 0xA5, 0xC3, 0x01, 0x01, 0x12 };

        var decoded = FrameCodec.TryDecode(tooShort, out int consumed);

        decoded.Should().BeNull();
        consumed.Should().Be(0);
    }

    [Fact]
    public void Encode_IntoProvidedBuffer_Works()
    {
        var frame = Frame.Data(new byte[] { 9, 9 }, TestSessionId, TestSequence);
        byte[] expected = FrameCodec.Encode(frame);

        var dest = new byte[expected.Length + 10]; // a bit oversized
        int written = FrameCodec.Encode(frame, dest);

        written.Should().Be(expected.Length);
        dest.AsSpan(0, written).ToArray().Should().Equal(expected);
    }

    [Fact]
    public void Encode_BufferTooSmall_Throws()
    {
        var frame = Frame.Data(new byte[50], TestSessionId, TestSequence);
        var smallBuffer = new byte[5];

        Action act = () => FrameCodec.Encode(frame, smallBuffer);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_PayloadLengthMismatch_Throws()
    {
        // Header claims 10 bytes but payload is only 2.
        var header = new FrameHeader(FrameHeader.CurrentVersion, FrameType.Data,
            TestSessionId, TestSequence, 10, FrameFlags.None);
        var badFrame = new Frame(header, new byte[] { 1, 2 });

        Action act = () => FrameCodec.Encode(badFrame);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_TwoConsecutiveFramesDecodeIndependently()
    {
        var f1 = Frame.Data(new byte[] { 0xAA }, TestSessionId, TestSequence, FrameFlags.Encrypted);
        var f2 = Frame.Data(new byte[] { 0xBB, 0xCC }, TestSessionId, TestSequence + 1, FrameFlags.None);

        byte[] combined = FrameCodec.Encode(f1).Concat(FrameCodec.Encode(f2)).ToArray();

        // Decode first frame.
        var d1 = FrameCodec.TryDecode(combined, out int c1);
        d1!.Payload.Should().Equal(new byte[] { 0xAA });
        d1.Flags.Should().Be(FrameFlags.Encrypted);

        // Decode second frame from the remainder.
        var remaining = combined.AsSpan(c1);
        var d2 = FrameCodec.TryDecode(remaining, out int c2);
        d2!.Payload.Should().Equal(new byte[] { 0xBB, 0xCC });
        d2.Sequence.Should().Be(TestSequence + 1);
        (c1 + c2).Should().Be(combined.Length);
    }
}
