using System.IO.Pipelines;
using AirCOM.Core.Protocol;
using FluentAssertions;
using Xunit;

namespace AirCOM.Core.Tests.Protocol;

/// <summary>
/// End-to-end stream tests through the Pipelines-based FrameReader/FrameWriter,
/// including resynchronization after junk bytes and frame splitting across chunks.
/// </summary>
public class FrameStreamTests
{
    [Fact]
    public async Task WriteRead_RoundTripsMultipleFrames()
    {
        var pipe = new Pipe();
        var writer = new FrameWriter(pipe.Writer);
        var reader = new FrameReader(pipe.Reader);

        var f1 = Frame.Data(new byte[] { 0x11, 0x22 }, 1, 1);
        var f2 = Frame.Data(new byte[] { 0x33 }, 1, 2);
        var f3 = Frame.Data(new byte[] { 0x44, 0x55, 0x66 }, 1, 3, FrameFlags.Encrypted);

        await writer.WriteAsync(f1);
        await writer.WriteAsync(f2);
        await writer.WriteAsync(f3);
        await writer.DisposeAsync(); // completes the writer -> reader sees EOF

        var d1 = await reader.ReadFrameAsync();
        var d2 = await reader.ReadFrameAsync();
        var d3 = await reader.ReadFrameAsync();
        var d4 = await reader.ReadFrameAsync(); // EOF

        d1!.Payload.Should().Equal(new byte[] { 0x11, 0x22 });
        d2!.Payload.Should().Equal(new byte[] { 0x33 });
        d3!.Flags.Should().Be(FrameFlags.Encrypted);
        d3.Payload.Should().Equal(new byte[] { 0x44, 0x55, 0x66 });
        d4.Should().BeNull();
        await reader.DisposeAsync();
    }

    [Fact]
    public async Task ReadFrame_SkipsLeadingJunkBytesBeforeMagic()
    {
        var pipe = new Pipe();
        var writer = new FrameWriter(pipe.Writer);
        var reader = new FrameReader(pipe.Reader);

        // Write a valid frame.
        var frame = Frame.Data(new byte[] { 0xAB, 0xCD }, 1, 1);
        await writer.WriteAsync(frame);

        // Prepend some junk bytes directly into the pipe before the frame.
        // Simulate this by writing junk first via the raw pipe then a real frame.
        // We re-create: write junk, then write frame, then complete.
        var pipe2 = new Pipe();
        var writer2 = new FrameWriter(pipe2.Writer);
        var reader2 = new FrameReader(pipe2.Reader);
        var junk = new byte[] { 0x00, 0xFF, 0x12, 0x34 }; // no magic here
        await pipe2.Writer.WriteAsync(junk);
        await writer2.WriteAsync(frame);
        await writer2.DisposeAsync();

        var decoded = await reader2.ReadFrameAsync();

        // Should have skipped the junk and found the real frame.
        decoded.Should().NotBeNull();
        decoded!.Payload.Should().Equal(new byte[] { 0xAB, 0xCD });
        await reader2.DisposeAsync();
    }
}
