using System.IO.Pipelines;
using AirCOM.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace AirCOM.Core.Transport;

/// <summary>
/// Decorates an <see cref="ITransport"/> with frame encoding/decoding. Exposes
/// SendFrameAsync / ReadFrameAsync so the bridge engine works in terms of
/// <see cref="Frame"/> objects rather than raw bytes.
///
/// Internally it bridges the ITransport byte stream to a <see cref="Pipe"/>:
/// a background pump fills the Pipe.Writer from the transport; FrameReader pulls
/// decoded frames from the Pipe.Reader. Writes go straight through FrameWriter to
/// the transport via its own short-lived buffer.
/// </summary>
public sealed class FramedConnection : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly ILogger<FramedConnection>? _logger;
    private readonly Pipe _receivePipe;
    private readonly FrameReader _frameReader;
    private readonly CancellationTokenSource _cts;
    private Task? _pumpTask;

    public FramedConnection(ITransport transport, ILogger<FramedConnection>? logger = null)
    {
        _transport = transport;
        _logger = logger;
        // PauseThreshold = flush once 64KB accumulated; ResumeThreshold resume writer at 32KB.
        _receivePipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: 1 << 16,
            resumeWriterThreshold: 1 << 15,
            writerScheduler: PipeScheduler.ThreadPool,
            readerScheduler: PipeScheduler.ThreadPool,
            useSynchronizationContext: false));
        _frameReader = new FrameReader(_receivePipe.Reader);
        _cts = new CancellationTokenSource();
    }

    /// <summary>Starts the background byte-pump from the transport into the receive pipe.</summary>
    public void StartReceivePump()
    {
        if (_pumpTask is not null) return;
        _pumpTask = Task.Run(() => ReceivePumpAsync(_cts.Token));
    }

    /// <summary>Reads the next decoded frame from the connection. Returns null on EOF.</summary>
    public Task<Frame?> ReadFrameAsync(CancellationToken ct = default) =>
        _frameReader.ReadFrameAsync(ct);

    /// <summary>Encodes and sends a frame.</summary>
    public async ValueTask SendFrameAsync(Frame frame, CancellationToken ct = default)
    {
        int wireSize = FrameHeader.WireSize(frame.Header.PayloadLength);
        // Encode into a scratch buffer, then write to the transport.
        byte[] scratch = new byte[wireSize];
        FrameCodec.Encode(frame, scratch);
        await _transport.WriteAsync(scratch, ct).ConfigureAwait(false);
    }

    /// <summary>Convenience: send a DATA frame from a raw payload.</summary>
    public ValueTask SendDataAsync(byte[] data, uint sessionId, uint sequence, CancellationToken ct = default) =>
        SendFrameAsync(Frame.Data(data, sessionId, sequence), ct);

    public ITransport Transport => _transport;

    /// <summary>Pumps raw bytes from the transport into the receive pipe, so FrameReader can parse them.</summary>
    private async Task ReceivePumpAsync(CancellationToken ct)
    {
        var writer = _receivePipe.Writer;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var buffer = writer.GetMemory(4096);
                int read = await _transport.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) break; // EOF

                writer.Advance(read);
                var flushResult = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (flushResult.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Receive pump error.");
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_pumpTask is not null) await _pumpTask.ConfigureAwait(false);
        }
        catch { }
        await _frameReader.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
