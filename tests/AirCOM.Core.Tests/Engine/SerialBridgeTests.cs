using System.IO.Pipelines;
using AirCOM.Core.Engine;
using AirCOM.Core.Transport;
using FluentAssertions;
using Xunit;

namespace AirCOM.Core.Tests.Engine;

/// <summary>
/// Tests the SerialBridge byte-pump without real network or serial hardware.
/// Two bridges are wired back-to-back through a shared in-memory Pipe pair
/// (one bridge's network output feeds the other's network input), and we
/// verify that bytes fed into one FakeSerialPort arrive at the other.
/// </summary>
public class SerialBridgeTests
{
    /// <summary>
    /// Creates a back-to-back bridge pair: bridgeA (serialA <-> pipeA->pipeB)
    /// and bridgeB (serialB <-> pipeB->pipeA). Bytes written to serialA's rx
    /// queue flow through bridgeA -> pipeA -> bridgeB -> serialB's Written list.
    /// </summary>
    private static (FramedConnection connA, FramedConnection connB) MakeBackToBackConnections()
    {
        // One pipe carries A->B, another B->A. Each FramedConnection reads from one
        // pipe's reader and writes to the other pipe's writer.
        var pipeAB = new Pipe(); // A writes, B reads
        var pipeBA = new Pipe(); // B writes, A reads

        // A "transport" that reads from one pipe and writes to another.
        var transportA = new PipeTransport(pipeBA.Reader, pipeAB.Writer);
        var transportB = new PipeTransport(pipeAB.Reader, pipeBA.Writer);
        var connA = new FramedConnection(transportA);
        var connB = new FramedConnection(transportB);
        connA.StartReceivePump();
        connB.StartReceivePump();
        return (connA, connB);
    }

    [Fact]
    public async Task Bridge_ForwardsSerialBytesToPeerAcrossNetwork()
    {
        var serialA = new FakeSerialPort("A");
        var serialB = new FakeSerialPort("B");
        var (connA, connB) = MakeBackToBackConnections();

        var bridgeA = new SerialBridge(serialA, connA, BridgeRole.ASide);
        var bridgeB = new SerialBridge(serialB, connB, BridgeRole.BSide);

        var payload = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
        serialA.Feed(payload);

        using var cts = new CancellationTokenSource();
        var runA = bridgeA.RunAsync(cts.Token);
        var runB = bridgeB.RunAsync(cts.Token);

        // Wait until B has received the bytes (poll the written list).
        var deadline = Environment.TickCount + 3000;
        while (serialB.Written.Count < payload.Length && Environment.TickCount < deadline)
        {
            await Task.Delay(20);
        }

        cts.Cancel();
        await Task.WhenAny(runA, Task.Delay(1000));
        await Task.WhenAny(runB, Task.Delay(1000));

        serialB.Written.Take(payload.Length).Should().Equal(payload);
        bridgeA.GetCurrentStats().BytesSerialToNet.Should().BeGreaterThanOrEqualTo(payload.Length);
        bridgeB.GetCurrentStats().BytesNetToSerial.Should().BeGreaterThanOrEqualTo(payload.Length);
    }

    [Fact]
    public async Task Bridge_Bidirectional_BothDirectionsForward()
    {
        var serialA = new FakeSerialPort("A");
        var serialB = new FakeSerialPort("B");
        var (connA, connB) = MakeBackToBackConnections();

        var bridgeA = new SerialBridge(serialA, connA, BridgeRole.ASide);
        var bridgeB = new SerialBridge(serialB, connB, BridgeRole.BSide);

        var aToB = new byte[] { 0xAA, 0xBB };
        var bToA = new byte[] { 0x01, 0x02, 0x03 };
        serialA.Feed(aToB);
        serialB.Feed(bToA);

        using var cts = new CancellationTokenSource();
        var runA = bridgeA.RunAsync(cts.Token);
        var runB = bridgeB.RunAsync(cts.Token);

        var deadline = Environment.TickCount + 3000;
        while ((serialB.Written.Count < aToB.Length || serialA.Written.Count < bToA.Length)
               && Environment.TickCount < deadline)
        {
            await Task.Delay(20);
        }

        cts.Cancel();
        await Task.WhenAny(runA, Task.Delay(1000));
        await Task.WhenAny(runB, Task.Delay(1000));

        serialB.Written.Take(aToB.Length).Should().Equal(aToB);
        serialA.Written.Take(bToA.Length).Should().Equal(bToA);
    }
}

/// <summary>
/// Minimal ITransport backed by a PipeReader/PipeWriter pair, for back-to-back
/// bridge testing without TCP. Reads consume from the reader; writes go to the writer.
/// </summary>
internal sealed class PipeTransport : ITransport
{
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    public ConnectionState State { get; private set; } = ConnectionState.Connected;
    public event EventHandler<ConnectionState>? StateChanged { add { } remove { } }
    public PipeTransport(PipeReader reader, PipeWriter writer) { _reader = reader; _writer = writer; }
    public ValueTask ConnectAsync(System.Net.EndPoint endpoint, CancellationToken ct = default) => ValueTask.CompletedTask;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var result = await _reader.ReadAsync(ct).ConfigureAwait(false);
        var seq = result.Buffer;
        if (seq.IsEmpty && result.IsCompleted) return 0;
        int n = (int)Math.Min(seq.Length, buffer.Length);
        // Copy the first n bytes from the sequence into the destination buffer.
        // Avoid Span<T> in the async method body (ref struct restriction).
        var slice = seq.Slice(0, n);
        byte[] tmp = new byte[n];
        int written = 0;
        foreach (var segment in slice)
        {
            for (int i = 0; i < segment.Length && written < n; i++)
            {
                tmp[written++] = segment.Span[i];
            }
            if (written >= n) break;
        }
        tmp.CopyTo(buffer);
        _reader.AdvanceTo(seq.GetPosition(n));
        return n;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var mem = _writer.GetMemory(data.Length);
        data.Span.CopyTo(mem.Span);
        _writer.Advance(data.Length);
        await _writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public ValueTask DisconnectAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() { _reader.Complete(); _writer.Complete(); return ValueTask.CompletedTask; }
}
