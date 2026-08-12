// AirCOM Loopback Test - M1 端到端自动化验证
//
// 这条命令跑完整个单机回环测试，不需要人工开串口软件：
//   1. 启动 EchoB（模拟 B 端，echo 收到的数据）
//   2. 启动 AirCOM A 端（开 COM11，连到 EchoB）
//   3. 用一个测试用的"串口软件"打开 COM10 写测试数据
//   4. 从 COM10 读回（数据经 com0com->COM11->A端->网络->EchoB->网络->A端->COM11->com0com->COM10）
//   5. 验证收到的数据 == 发送的数据
//
// 用法：dotnet run --project tests/AirCOM.LoopbackTest

using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using AirCOM.Client.A.Services;
using AirCOM.Core.Protocol;

namespace AirCOM.LoopbackTest;

internal static class Program
{
    private static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== AirCOM M1 单机回环测试 ===");

        const string virtualPortA = "COM11";   // A 端服务打开
        const string userPortA = "COM10";      // 测试"串口软件"打开
        const int tcpPort = 51001;             // EchoB 监听端口
        var initialParams = SerialParams.Default;

        // 1. 启动 EchoB
        Console.WriteLine("[1/5] 启动 EchoB (模拟 B 端)...");
        var echoB = new EchoBProcess();
        var echoTask = echoB.StartAsync(tcpPort);
        await Task.Delay(300);

        try
        {
            // 2. 启动 A 端
            Console.WriteLine("[2/5] 启动 AirCOM A 端...");
            await using var aHost = new AEngineHost();
            aHost.Stopped += (s, ex) => Console.WriteLine($"  A端停止: {(ex is null ? "正常" : ex.Message)}");
            await aHost.StartAsync(virtualPortA, initialParams, IPAddress.Loopback, tcpPort);
            Console.WriteLine("  A端已连接 EchoB");
            await Task.Delay(300);

            // 3. 打开 COM10（模拟用户的串口软件）
            Console.WriteLine($"[3/5] 打开 {userPortA} (模拟用户串口软件)...");
            using var userPort = new SerialPort(userPortA, 115200);
            userPort.ReadTimeout = 5000;
            userPort.Open();
            await Task.Delay(200);

            // 4. 发送测试数据
            var testData = System.Text.Encoding.ASCII.GetBytes("AirCOM-M1-loopback-OK-1234567890");
            Console.WriteLine($"[4/5] 向 {userPortA} 发送 {testData.Length} 字节测试数据...");
            userPort.Write(testData, 0, testData.Length);

            // 5. 读回并验证
            Console.WriteLine($"[5/5] 从 {userPortA} 读回数据（等待 echo）...");
            var buf = new byte[256];
            int totalRead = 0;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (totalRead < testData.Length && DateTime.UtcNow < deadline)
            {
                try
                {
                    int n = userPort.Read(buf, totalRead, buf.Length - totalRead);
                    if (n > 0) totalRead += n;
                }
                catch (TimeoutException) { break; }
            }

            var received = buf.AsSpan(0, totalRead).ToArray();
            Console.WriteLine($"  收到 {totalRead} 字节: {System.Text.Encoding.ASCII.GetString(received)}");

            bool pass = received.SequenceEqual(testData);
            Console.WriteLine();
            Console.WriteLine(pass ? ">>> 测试通过：A 端完整数据通路工作正常！<<<"
                                   : ">>> 测试失败：收到的数据与发送的不一致 <<<");
            if (!pass)
            {
                Console.WriteLine($"  发送: {BitConverter.ToString(testData)}");
                Console.WriteLine($"  收到: {BitConverter.ToString(received)}");
            }
        }
        finally
        {
            echoB.Stop();
            try { await echoTask; } catch { }
        }
    }
}

/// <summary>内嵌的 EchoB 服务：监听一个端口，把收到的 DATA 帧原样回送。</summary>
internal sealed class EchoBProcess
{
    private TcpListener? _listener;
    private CancellationTokenSource _cts = new();

    public Task StartAsync(int port)
    {
        _listener = new TcpListener(System.Net.IPAddress.Any, port);
        _listener.Start();
        return Task.Run(() => AcceptLoop(_cts.Token));
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch { return; }

            _ = Task.Run(async () =>
            {
                await using var transport = new AirCOM.Core.Transport.TcpTransport(client);
                await using var framed = new AirCOM.Core.Transport.FramedConnection(transport);
                framed.StartReceivePump();
                uint echoSeq = 1;
                while (true)
                {
                    Frame? f;
                    try { f = await framed.ReadFrameAsync(); }
                    catch { break; }
                    if (f is null) break;
                    if (f.Type == FrameType.Data)
                        await framed.SendFrameAsync(Frame.Data(f.Payload, f.SessionId, echoSeq++));
                }
            }, ct);
        }
    }

    public void Stop() { _cts.Cancel(); _listener?.Stop(); }
}
