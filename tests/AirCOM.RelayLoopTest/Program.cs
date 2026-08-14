// AirCOM Relay Loop Test - M2 端到端回环验证
//
// 全部在本机跑：起 SignalRelayServer + 模拟 B 端客户端（AUTH 配对注册）+
// 模拟 A 端客户端（AUTH 配对请求）→ 服务器匹配 → MATCHED → 中继管道建立 →
// A 发 DATA 帧到 B、B 回 DATA 帧到 A，验证双向透传。
//
// 用法：dotnet run --project tests/AirCOM.RelayLoopTest [port]

using System.Net;
using System.Net.Sockets;
using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Transport;
using AirCOM.Server;

namespace AirCOM.RelayLoopTest;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 51990;
        Console.WriteLine($"=== AirCOM M2 信令+中继 回环测试（端口 {port}）===");

        // 1. 起服务器
        Console.WriteLine("[1/6] 启动服务器...");
        using var serverCts = new CancellationTokenSource();
        var server = new SignalRelayServer(port);
        var serverTask = server.RunAsync(serverCts.Token);
        await Task.Delay(400); // listener 起来

        var pairingCode = "123456";
        try
        {
            // 2. B 端客户端连接 + 注册配对码
            Console.WriteLine("[2/6] B 端连接服务器，注册配对码...");
            var bConn = await ConnectPeerAsync(port, pairingCode, isBSide: true);

            // 3. A 端客户端连接 + 提交配对码（应触发匹配）
            Console.WriteLine("[3/6] A 端连接服务器，提交配对码...");
            var aConn = await ConnectPeerAsync(port, pairingCode, isBSide: false);

            // 4. 双端应各收到 MATCHED
            Console.WriteLine("[4/6] 等待双端收到 MATCHED...");
            var aMatched = await ExpectMatchedAsync(aConn, "A");
            var bMatched = await ExpectMatchedAsync(bConn, "B");
            if (aMatched != bMatched)
            {
                Console.WriteLine($">>> 失败：A/B 会话 ID 不一致（{aMatched} vs {bMatched}）");
                return 1;
            }
            Console.WriteLine($"    会话 {aMatched} 匹配成功");

            // 5. A -> B 发 DATA 帧，B 应原样收到
            Console.WriteLine("[5/6] A->B 发送 DATA 帧...");
            var payloadA = "hello-from-A"u8.ToArray();
            await aConn.SendFrameAsync(Frame.Data(payloadA, 1, 1));
            var frameAtB = await bConn.ReadFrameAsync();
            if (frameAtB is null || frameAtB.Type != FrameType.Data ||
                !frameAtB.Payload.AsSpan().SequenceEqual(payloadA))
            {
                Console.WriteLine($">>> 失败：B 收到的帧不对（{(frameAtB is null ? "EOF" : frameAtB.Type.ToString())}）");
                return 1;
            }
            Console.WriteLine($"    B 收到 {frameAtB.Payload.Length} 字节");

            // 6. B -> A 回 DATA 帧，A 应原样收到
            Console.WriteLine("[6/6] B->A 回发 DATA 帧...");
            var payloadB = "echo-from-B-9876543210"u8.ToArray();
            await bConn.SendFrameAsync(Frame.Data(payloadB, 1, 2));
            var frameAtA = await aConn.ReadFrameAsync();
            if (frameAtA is null || frameAtA.Type != FrameType.Data ||
                !frameAtA.Payload.AsSpan().SequenceEqual(payloadB))
            {
                Console.WriteLine($">>> 失败：A 收到的帧不对（{(frameAtA is null ? "EOF" : frameAtA.Type.ToString())}）");
                return 1;
            }
            Console.WriteLine($"    A 收到 {frameAtA.Payload.Length} 字节");

            Console.WriteLine();
            Console.WriteLine(">>> 测试通过：信令配对 + 中继双向透传 全部正常 <<<");
            return 0;
        }
        finally
        {
            serverCts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    /// <summary>连上服务器并发 AUTH（配对码）。角色编码在 sessionId 高位：B=1,A=0。</summary>
    private static async Task<FramedConnection> ConnectPeerAsync(int port, string code, bool isBSide)
    {
        var transport = new TcpTransport();
        await transport.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
        var framed = new FramedConnection(transport);
        framed.StartReceivePump();

        var auth = AuthMessage.FromPairingCode(code, (uint)(isBSide ? 0x80000001 : 1));
        var payload = auth.ToPayload();
        await framed.SendFrameAsync(new Frame(
            new FrameHeader(FrameHeader.CurrentVersion, FrameType.Auth, 0, 0,
                (ushort)payload.Length, FrameFlags.None), payload));
        return framed;
    }

    /// <summary>等待 MATCHED 帧，返回 sessionId；收到 SIGNALING_ERROR 则直接失败退出。</summary>
    private static async Task<uint> ExpectMatchedAsync(FramedConnection conn, string tag)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var frame = await conn.ReadFrameAsync();
            if (frame is null) throw new InvalidOperationException($"{tag}: EOF before MATCHED");
            switch (frame.Type)
            {
                case FrameType.Matched:
                    return ((MatchedMessage)MessageCodec.Decode(frame)!).SessionId;
                case FrameType.SignalingError:
                    var err = (SignalingErrorMessage)MessageCodec.Decode(frame)!;
                    throw new InvalidOperationException($"{tag}: server error {err.ErrorCode}: {err.Message}");
                default:
                    Console.WriteLine($"    {tag}: 忽略帧 {frame.Type}");
                    break;
            }
        }
        throw new InvalidOperationException($"{tag}: MATCHED 超时");
    }
}
