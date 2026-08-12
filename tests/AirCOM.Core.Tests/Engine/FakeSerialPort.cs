using AirCOM.Core.Protocol;
using AirCOM.Core.Serial;

namespace AirCOM.Core.Tests.Engine;

/// <summary>
/// In-memory fake ISerialPort for testing the bridge without real hardware.
/// Reads drain a producer queue; writes append to a captured list. No control
/// lines are exercised by this fake (returns None), keeping tests focused on
/// the byte-pump data path.
/// </summary>
internal sealed class FakeSerialPort : ISerialPort
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte> _rxQueue = new();
    public bool IsOpen { get; private set; } = true;
    public string PortName { get; }
    public List<byte> Written { get; } = new();
    public SerialParams LastParams { get; private set; }

    public FakeSerialPort(string name = "FAKE") { PortName = name; }

    /// <summary>Enqueues bytes that the bridge's read pump will consume.</summary>
    public void Feed(byte[] data)
    {
        foreach (var b in data) _rxQueue.Enqueue(b);
    }

    public Task OpenAsync(SerialParams parameters, CancellationToken ct = default)
    {
        LastParams = parameters;
        IsOpen = true;
        return Task.CompletedTask;
    }

    public void SetParams(SerialParams parameters) => LastParams = parameters;
    public void SetControlLines(ModemStatusFlags output) { }
    public ModemStatusFlags GetControlLines() => ModemStatusFlags.None;
    public void Break(ushort durationMs) { }
    public event EventHandler<ModemStatusFlags>? ControlLinesChanged { add { } remove { } }

    public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        int n = 0;
        while (n < count && _rxQueue.TryDequeue(out var b))
        {
            buffer[offset + n] = b;
            n++;
        }
        // If nothing available, yield once so the pump doesn't spin; tests feed data first.
        return Task.FromResult(n);
    }

    public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        var copy = new byte[count];
        Array.Copy(buffer, offset, copy, 0, count);
        Written.AddRange(copy);
        return Task.CompletedTask;
    }

    public void Dispose() { IsOpen = false; }
}
