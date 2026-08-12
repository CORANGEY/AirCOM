// AirCOM Echo B-side - M1 单机回环测试用的简易 B 端
//
// 用途：在没有第二对虚拟口或 USB-TTL 时，验证 A 端完整数据通路。
// 工作方式：监听一个 TCP 端口，接受 A 端连接，把收到的任何 DATA 帧原样回送（echo）。
// 这样 A 端：串口软件 COM10 写 -> com0com -> COM11 -> A端服务 -> 网络 -> 本程序 ->
//          echo -> 网络 -> A端服务 -> COM11 -> com0com -> COM10 -> 串口软件收到
//
// 用法：dotnet run --project src/AirCOM.EchoB
// 然后启动 AirCOM A 端，连接 127.0.0.1:<端口>（默认 51000）

using System.Net;
using System.Net.Sockets;
using AirCOM.Core.Protocol;
using AirCOM.Core.Transport;

namespace AirCOM.EchoB;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 51000;
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"Echo B-side listening on port {port}. A-side connect to 127.0.0.1:{port}");
        Console.WriteLine("Will echo back any DATA frame received. Press Ctrl+C to stop.");

        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch (Exception ex) { Console.WriteLine($"Accept error: {ex.Message}"); continue; }

            Console.WriteLine("A-side connected.");
            _ = HandleClientAsync(client);
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        await using var transport = new TcpTransport(client);
        await using var framed = new FramedConnection(transport);
        framed.StartReceivePump();

        uint echoSeq = 1;
        while (true)
        {
            Frame? frame;
            try { frame = await framed.ReadFrameAsync(); }
            catch (Exception ex) { Console.WriteLine($"Read error: {ex.Message}"); break; }
            if (frame is null) { Console.WriteLine("A-side disconnected."); break; }

            if (frame.Type == FrameType.Data)
            {
                Console.WriteLine($"  echo {frame.Payload.Length} bytes (seq {frame.Sequence})");
                // Echo the payload straight back with a new sequence.
                await framed.SendFrameAsync(Frame.Data(frame.Payload, frame.SessionId, echoSeq++));
            }
            else
            {
                Console.WriteLine($"  (frame type {frame.Type}, ignoring)");
            }
        }
    }
}
