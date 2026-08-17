// AirCOM Network Mode Loop Test - M2 客户端网络模式端到端验证
//
// 本机模拟完整跨网流程：服务器 + B 端客户端（配对注册 + 真实桥接到 FakeSerial）
// + A 端客户端（配对 + 虚拟口桥接）。数据路径：
//   A端虚拟口写 ──► A桥接 ──► 中继服务器 ──► B桥接 ──► FakeSerial(设备)
//   FakeSerial(设备回显) ──► B桥接 ──► 服务器 ──► A桥接 ──► A端虚拟口读
//
// 用法：dotnet run --project tests/AirCOM.NetworkLoopTest

using System.Net;
using AirCOM.App.Services;
using AirCOM.Core.Engine;
using AirCOM.Core.Protocol;
using AirCOM.Core.Transport;
using AirCOM.Server;

namespace AirCOM.NetworkLoopTest;

internal static class Program
{
    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int port = 51991;
        Console.WriteLine($"=== AirCOM M2 网络模式端到端回环（端口 {port}）===");

        // 1. 服务器
        Console.WriteLine("[1/5] 启动服务器...");
        using var serverCts = new CancellationTokenSource();
        var server = new SignalRelayServer(port);
        var serverTask = server.RunAsync(serverCts.Token);
        await Task.Delay(400);

        // 用 FakeSerial 模拟 B 端设备（不回显；回复由测试手动注入，避免回显数据干扰断言）
        var fakeDevice = new EchoDevice(echo: false);

        try
        {
            // 2. B 端：配对 + 桥接（设备 = FakeSerial）
            Console.WriteLine("[2/5] B 端配对并桥接...");
            var pairingCode = "654321";
            var bPairing = new NetworkPairingClient();
            var bConnTask = bPairing.PairAsync(new IPEndPoint(IPAddress.Loopback, port), pairingCode, isBSide: true);
            // 给服务器一点时间注册 B
            await Task.Delay(300);

            var bBridge = new TaskCompletionSource<BridgeShell>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bRunTask = Task.Run(async () =>
            {
                var conn = await bConnTask;
                Console.WriteLine("    B 端配对成功");
                var shell = new BridgeShell(fakeDevice, conn, BridgeRole.BSide);
                bBridge.SetResult(shell);
                await shell.RunAsync();
            });

            // 3. A 端：配对
            Console.WriteLine("[3/5] A 端配对...");
            var aPairing = new NetworkPairingClient();
            var aConn = await aPairing.PairAsync(new IPEndPoint(IPAddress.Loopback, port), pairingCode, isBSide: false);
            Console.WriteLine("    A 端配对成功");

            // A 端桥接：A 侧“串口”是数据源（不回显）
            var aSideSerial = new EchoDevice(echo: false);
            var aShell = new BridgeShell(aSideSerial, aConn, BridgeRole.ASide);
            var aRunTask = Task.Run(() => aShell.RunAsync());
            await Task.Delay(500); // 让两端泵都跑起来

            // 4. A -> 服务器 -> B：往 A 侧串口喂数据，B 端设备应收到
            Console.WriteLine("[4/5] A→B 数据穿透...");
            var sent = "cross-network-hello-A2B"u8.ToArray();
            aSideSerial.Inject(sent);
            var gotAtB = await fakeDevice.CollectAsync(sent.Length, 5000);
            if (!gotAtB.SequenceEqual(sent))
            {
                Console.WriteLine($">>> 失败：B 端设备收到 {gotAtB.Length} 字节（期望 {sent.Length}）");
                return 1;
            }
            Console.WriteLine($"    B 端设备收到 {gotAtB.Length} 字节 ✓");

            // 5. B -> 服务器 -> A：设备回发，A 侧串口应收到
            Console.WriteLine("[5/5] B→A 数据穿透...");
            var reply = "device-reply-B2A-9876"u8.ToArray();
            fakeDevice.Inject(reply);
            var gotAtA = await aSideSerial.CollectAsync(reply.Length, 5000);
            if (!gotAtA.SequenceEqual(reply))
            {
                Console.WriteLine($">>> 失败：A 端收到 {gotAtA.Length} 字节（期望 {reply.Length}）");
                return 1;
            }
            Console.WriteLine($"    A 端收到 {gotAtA.Length} 字节 ✓");

            Console.WriteLine();
            Console.WriteLine(">>> 测试通过：网络模式 配对 + 中继 + 双向数据穿透 全部正常 <<<");
            return 0;
        }
        finally
        {
            serverCts.Cancel();
            try { await serverTask; } catch { }
        }
    }
}

/// <summary>A fake ISerialPort. As a device (B-side): echoes written bytes back.
/// As a data source (A-side): pass echo=false; written bytes are just recorded, injected bytes are readable.</summary>
internal sealed class EchoDevice : AirCOM.Core.Serial.ISerialPort
{
    private readonly bool _echo;
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte> _rx = new(); // bytes available to read (injected)
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte> _written = new(); // bytes written to device
    public bool IsOpen { get; private set; } = true;
    public string PortName { get; } = "FAKE-DEVICE";
    public int BytesToRead => _rx.Count;
    public event EventHandler<AirCOM.Core.Serial.ModemStatusFlags>? ControlLinesChanged { add { } remove { } }

    public EchoDevice(bool echo = true) { _echo = echo; }

    public Task OpenAsync(SerialParams p, CancellationToken ct = default) { IsOpen = true; return Task.CompletedTask; }
    public void SetParams(SerialParams p) { }
    public void SetControlLines(AirCOM.Core.Serial.ModemStatusFlags f) { }
    public AirCOM.Core.Serial.ModemStatusFlags GetControlLines() => AirCOM.Core.Serial.ModemStatusFlags.None;
    public void Break(ushort ms) { }

    /// <summary>Test injects bytes as if the device transmitted them.</summary>
    public void Inject(byte[] data) { foreach (var b in data) _rx.Enqueue(b); }

    /// <summary>Waits until the device has received n bytes, returns them.</summary>
    public async Task<byte[]> CollectAsync(int n, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (_written.Count >= n) break;
            await Task.Delay(50);
        }
        var result = new byte[n];
        for (int i = 0; i < n; i++) _written.TryDequeue(out result[i]);
        return result;
    }

    // Bridge reads: data the device "sends" (injected). Written bytes recorded; optionally echoed back.
    public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        int n = 0;
        while (n < count && _rx.TryDequeue(out var b)) buffer[offset + n++] = b;
        return Task.FromResult(n);
    }

    public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct = default)
    {
        var copy = new byte[count];
        Array.Copy(buffer, offset, copy, 0, count);
        foreach (var b in copy)
        {
            _written.Enqueue(b);
            if (_echo) _rx.Enqueue(b); // device echoes back
        }
        return Task.CompletedTask;
    }

    public void Dispose() { IsOpen = false; }
}

/// <summary>Wires a serial port and connection into a running SerialBridge.</summary>
internal sealed class BridgeShell
{
    private readonly AirCOM.Core.Serial.ISerialPort _serial;
    private readonly FramedConnection _conn;
    private readonly BridgeRole _role;

    public BridgeShell(AirCOM.Core.Serial.ISerialPort serial, FramedConnection conn, BridgeRole role)
    {
        _serial = serial;
        _conn = conn;
        _role = role;
    }

    public Task RunAsync() => new SerialBridge(_serial, _conn, _role).RunAsync(CancellationToken.None);
}
