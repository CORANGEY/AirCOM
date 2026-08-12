using System.Buffers;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using AirCOM.Core.Protocol;

namespace AirCOM.Core.Protocol;

/// <summary>
/// Reads framed messages from a <see cref="PipeReader"/>-backed byte stream.
/// Handles magic resynchronization (skipping stray bytes until a frame boundary is found),
/// partial-frame buffering, and CRC validation.
/// </summary>
public sealed class FrameReader : IAsyncDisposable
{
    private readonly PipeReader _reader;
    private readonly ILogger<FrameReader> _logger;

    public FrameReader(PipeReader reader, ILogger<FrameReader>? logger = null)
    {
        _reader = reader;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FrameReader>.Instance;
    }

    /// <summary>
    /// Reads the next complete frame. Returns null when the stream ends cleanly.
    /// Throws <see cref="InvalidDataException"/> on an unrecoverable framing error.
    /// </summary>
    public async Task<Frame?> ReadFrameAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var result = await _reader.ReadAsync(ct).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
                return null;

            // Try to decode a frame from the front of the buffer.
            if (TryReadFrame(ref buffer, out var frame))
            {
                _reader.AdvanceTo(buffer.Start);
                return frame;
            }

            // If more data is still coming, mark what we've examined so we get notified on arrival.
            _reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted && buffer.IsEmpty)
                return null;
        }
    }

    private bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out Frame? frame)
    {
        frame = null;

        // Need at least enough bytes to inspect the header.
        if (buffer.Length < FrameHeader.HeaderSize)
            return false;

        var headerSpan = buffer.Slice(0, FrameHeader.HeaderSize).ToArray();

        // Resync: scan for the magic marker, discarding leading junk bytes.
        int magicOffset = FindMagic(headerSpan);
        if (magicOffset < 0)
        {
            // No magic in this batch; keep only the last (HeaderSize-1) bytes in case
            // the magic straddles a chunk boundary, discard the rest.
            int keep = (int)Math.Min(buffer.Length, FrameHeader.HeaderSize - 1);
            var keepPos = buffer.GetPosition(buffer.Length - keep);
            buffer = buffer.Slice(keepPos);
            _logger.LogDebug("No magic found; resyncing (discarded {Discarded} bytes).", buffer.Length - keep);
            return false;
        }

        if (magicOffset > 0)
        {
            // Discard bytes before the magic.
            buffer = buffer.Slice(magicOffset);
            headerSpan = buffer.Slice(0, FrameHeader.HeaderSize).ToArray();
        }

        // Read the payload length to know the full wire size.
        ushort payloadLength = FrameCodec.ReadUInt16Big(headerSpan, 12);
        int wireSize = FrameHeader.WireSize(payloadLength);

        if (buffer.Length < wireSize)
            return false; // Wait for the rest of the frame.

        // Copy the full frame out of the sequence.
        var frameBytes = buffer.Slice(0, wireSize).ToArray();
        var decoded = FrameCodec.TryDecode(frameBytes, out int consumed);
        if (decoded == null || consumed == 0)
        {
            // CRC failed or version mismatch: skip one byte and resync.
            _logger.LogWarning("Frame decode failed at offset {Offset}; resyncing.", magicOffset);
            buffer = buffer.Slice(1);
            return false;
        }

        // Advance past the consumed frame.
        buffer = buffer.Slice(consumed);
        frame = decoded;
        return true;
    }

    private static int FindMagic(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i <= span.Length - 2; i++)
        {
            if (FrameCodec.ReadUInt16Big(span, i) == FrameHeader.Magic)
                return i;
        }
        return -1;
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.CompleteAsync().ConfigureAwait(false);
    }
}
