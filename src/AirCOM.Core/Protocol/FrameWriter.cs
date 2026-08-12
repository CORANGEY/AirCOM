using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol;

/// <summary>
/// Writes framed messages to a <see cref="PipeWriter"/>-backed byte stream.
/// Encodes the full header + payload + CRC16 in one contiguous span write.
/// </summary>
public sealed class FrameWriter : IAsyncDisposable
{
    private readonly PipeWriter _writer;
    private readonly ILogger<FrameWriter> _logger;

    public FrameWriter(PipeWriter writer, ILogger<FrameWriter>? logger = null)
    {
        _writer = writer;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FrameWriter>.Instance;
    }

    /// <summary>Encodes and writes a frame, then flushes.</summary>
    public async ValueTask WriteAsync(Frame frame, CancellationToken ct = default)
    {
        int wireSize = FrameHeader.WireSize(frame.Header.PayloadLength);
        // Encode into a scratch array, then copy into the pipe. This avoids holding a
        // ref struct (Span<T>) across the async state machine, which C# disallows.
        byte[] scratch = new byte[wireSize];
        try
        {
            int written = FrameCodec.Encode(frame, scratch);
            var dest = _writer.GetMemory(written);
            scratch.AsSpan(0, written).CopyTo(dest.Span);
            _writer.Advance(written);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encode frame (type={Type}, payloadLen={Len}).",
                frame.Type, frame.Header.PayloadLength);
            throw;
        }
        await _writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Convenience: build and write a DATA frame from a raw payload.</summary>
    public ValueTask WriteDataAsync(byte[] data, uint sessionId, uint sequence, CancellationToken ct = default) =>
        WriteAsync(Frame.Data(data, sessionId, sequence), ct);

    public async ValueTask DisposeAsync()
    {
        await _writer.CompleteAsync().ConfigureAwait(false);
    }
}
