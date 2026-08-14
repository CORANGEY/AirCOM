using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using AirCOM.Core.Protocol;
using AirCOM.Core.Protocol.Messages;
using AirCOM.Core.Transport;
using AirCOM.Core.Util;

namespace AirCOM.Server;

/// <summary>
/// One TCP listener implementing both roles:
///  - Signaling: peers authenticate with a 6-digit pairing code (AUTH frame, pairing
///    mode); the first pending B-side registration is matched with the first A-side
///    request carrying the same code; both get MATCHED.
///  - Relay: after MATCHED, both connections are joined into a dumb byte-pipe. The
///    frames flowing through are the existing A<->B protocol frames (DATA, SET_PARAMS,
///    LINE_STATE...) - the server never parses them, just forwards bytes.
/// </summary>
public sealed class SignalRelayServer
{
    private readonly int _port;
    private readonly TimeSpan _pairingWaitTimeout = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _idleSessionTimeout = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, PendingPeer> _pendingB = new();
    private readonly ConcurrentDictionary<uint, RelaySession> _sessions = new();
    private uint _nextSessionId = 1;
    private readonly object _sessionIdLock = new();

    public SignalRelayServer(int port)
    {
        _port = port;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        DiagLog.Log($"Server listening on port {_port}");
        Console.WriteLine($"[AirCOM Server] listening on port {_port}");

        // Periodic cleanup: expire stale pending registrations and idle sessions.
        _ = Task.Run(() => CleanupLoop(ct), ct);

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        DiagLog.Log($"Server: connection from {remote}");
        try
        {
            client.NoDelay = true;
            await using var transport = new TcpTransport(client);
            await using var framed = new FramedConnection(transport);
            framed.StartReceivePump();

            // --- Signaling phase: expect exactly one AUTH frame (pairing mode). ---
            using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            authCts.CancelAfter(TimeSpan.FromSeconds(30));
            Frame? authFrame;
            try { authFrame = await framed.ReadFrameAsync(authCts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                DiagLog.Log($"Server: {remote} auth timeout");
                return;
            }
            if (authFrame is null || authFrame.Type != FrameType.Auth)
            {
                DiagLog.Log($"Server: {remote} first frame not AUTH ({authFrame?.Type.ToString() ?? "EOF"})");
                return;
            }

            var auth = (AuthMessage)MessageCodec.Decode(authFrame)!;
            if (auth.Mode != AuthMessage.AuthMode.PairingCode)
            {
                await SendErrorAsync(framed, SignalingErrorMessage.CodeBadPairingCode, "expected pairing-code auth", ct);
                return;
            }
            var code = System.Text.Encoding.ASCII.GetString(auth.Credential);
            if (code.Length != 6 || !code.All(char.IsDigit))
            {
                await SendErrorAsync(framed, SignalingErrorMessage.CodeBadPairingCode, "pairing code must be 6 digits", ct);
                return;
            }
            // role: session-id high bit 0 = A-side (consumer), 1 = B-side (provider)
            bool isBSide = (auth.SessionId & 0x80000000) != 0;

            DiagLog.Log($"Server: {remote} AUTH code={code} role={(isBSide ? "B" : "A")}");

            // --- Pairing/matching phase ---
            if (isBSide)
            {
                // Register and wait for an A-side to match.
                var pending = new PendingPeer(framed, code);
                // One B registration per code: replace stale.
                if (_pendingB.TryRemove(code, out var stale)) stale.Cts.Cancel();
                _pendingB[code] = pending;
                try
                {
                    // Wait until Matched (peerA set) or timeout/cancel.
                    while (pending.PeerA is null && !pending.Cts.IsCancellationRequested)
                        await Task.Delay(200, pending.Cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }

                if (!_pendingB.TryRemove(code, out var registered) || registered != pending)
                {
                    // Replaced or cancelled.
                    if (pending.PeerA is null)
                    {
                        _pendingB.TryRemove(new KeyValuePair<string, PendingPeer>(code, pending));
                        await SendErrorAsync(framed, SignalingErrorMessage.CodePairingTimeout, "pairing expired", CancellationToken.None);
                        return;
                    }
                }
                if (pending.PeerA is null)
                {
                    await SendErrorAsync(framed, SignalingErrorMessage.CodePairingTimeout, "pairing timeout", CancellationToken.None);
                    return;
                }

                // Matched: send MATCHED to both, join into relay session.
                var sessionId = NextSessionId();
                await SendMatchedAsync(framed, sessionId, CancellationToken.None);
                await SendMatchedAsync(pending.PeerA.framed, sessionId, CancellationToken.None);
                DiagLog.Log($"Server: session {sessionId} matched via code {code}");
                var session = new RelaySession(sessionId, framed, pending.PeerA.framed);
                _sessions[sessionId] = session;
                await session.RunAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                // A-side: wait for a pending B with this code. The B-side may register
                // slightly later (race at connect time, or the user enters the code on
                // A before B clicks share), so poll briefly instead of failing fast.
                PendingPeer? pending = null;
                var waitDeadline = Environment.TickCount64 + 10_000; // 10s
                while (!ct.IsCancellationRequested)
                {
                    if (_pendingB.TryGetValue(code, out var candidate) && candidate.PeerA is null)
                    {
                        pending = candidate;
                        break;
                    }
                    if (Environment.TickCount64 > waitDeadline)
                    {
                        await SendErrorAsync(framed, SignalingErrorMessage.CodeBadPairingCode,
                            "no B-side waiting with this code", ct);
                        return;
                    }
                    try { await Task.Delay(200, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
                if (pending is null) return;

                // Claim it (only one A wins).
                var claimant = new PendingPeer(framed, code);
                if (Interlocked.CompareExchange(ref pending.Claim, claimant, null) is not null)
                {
                    await SendErrorAsync(framed, SignalingErrorMessage.CodeBadPairingCode, "code already claimed", ct);
                    return;
                }
                pending.PeerA = claimant;
                pending.Cts.Cancel(); // wake the waiting B-side loop

                // B-side handler now drives the session (it owns both connections).
                // This A-side handler just outlives its socket: when the session ends it
                // disposes both connections, our transport goes Disconnected, and we exit.
                while (framed.Transport.State == ConnectionState.Connected)
                {
                    await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.Log($"Server: {remote} error {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            DiagLog.Log($"Server: {remote} disconnected");
        }
    }

    private uint NextSessionId()
    {
        lock (_sessionIdLock) { return _nextSessionId++; }
    }

    private static async ValueTask SendMatchedAsync(FramedConnection framed, uint sessionId, CancellationToken ct)
    {
        var payload = new MatchedMessage(sessionId).ToPayload();
        await framed.SendFrameAsync(new Frame(
            new FrameHeader(FrameHeader.CurrentVersion, FrameType.Matched, 0, 0,
                (ushort)payload.Length, FrameFlags.None), payload), ct);
    }

    private static async ValueTask SendErrorAsync(FramedConnection framed, ushort code, string msg, CancellationToken ct)
    {
        try
        {
            var payload = new SignalingErrorMessage(code, msg).ToPayload();
            await framed.SendFrameAsync(new Frame(
                new FrameHeader(FrameHeader.CurrentVersion, FrameType.SignalingError, 0, 0,
                    (ushort)payload.Length, FrameFlags.None), payload), ct);
        }
        catch { }
    }

    private async Task CleanupLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            var now = Environment.TickCount64;
            foreach (var (code, p) in _pendingB)
            {
                if (now - p.RegisteredAt > _pairingWaitTimeout.TotalMilliseconds && p.PeerA is null)
                {
                    if (_pendingB.TryRemove(code, out var removed))
                    {
                        removed.Cts.Cancel();
                        DiagLog.Log($"Server: expired pending B registration code={code}");
                    }
                }
            }
            foreach (var (id, s) in _sessions)
            {
                if (now - s.LastActivityTick > _idleSessionTimeout.TotalMilliseconds && !s.IsAlive)
                {
                    _sessions.TryRemove(id, out _);
                }
            }
        }
    }

    /// <summary>A B-side connection waiting for a matching A-side.</summary>
    private sealed class PendingPeer
    {
        public readonly FramedConnection FramedConn;
        public readonly string Code;
        public readonly CancellationTokenSource Cts = new();
        public readonly long RegisteredAt = Environment.TickCount64;
        public PendingPeer? PeerA;               // set when an A-side claims this registration
        public PendingPeer? Claim;               // CAS slot for the claiming A-side

        public PendingPeer(FramedConnection framed, string code)
        {
            FramedConn = framed;
            Code = code;
        }

        // Expose framed as field-friendly accessor used above.
        public FramedConnection framed => FramedConn;
    }

    /// <summary>Two joined connections relaying frames both ways until either closes.</summary>
    private sealed class RelaySession
    {
        private readonly uint _id;
        private readonly FramedConnection _a; // A-side (consumer)
        private readonly FramedConnection _b; // B-side (provider)
        public long LastActivityTick = Environment.TickCount64;
        public bool IsAlive = true;

        public RelaySession(uint id, FramedConnection bFramed, FramedConnection aFramed)
        {
            _id = id;
            _b = bFramed;
            _a = aFramed;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            // Two pump tasks; when either ends, close both transports so the peers'
            // existing disconnect-detection kicks in.
            var pumpAB = PumpAsync(_a, _b, "A->B", ct);
            var pumpBA = PumpAsync(_b, _a, "B->A", ct);
            var first = await Task.WhenAny(pumpAB, pumpBA).ConfigureAwait(false);
            IsAlive = false;
            LastActivityTick = Environment.TickCount64;
            DiagLog.Log($"Server: session {_id} ended");
            try { await _a.DisposeAsync(); } catch { }
            try { await _b.DisposeAsync(); } catch { }
            try { await first; } catch { }
        }

        private async Task PumpAsync(FramedConnection from, FramedConnection to, string tag, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                Frame? frame;
                try { frame = await from.ReadFrameAsync(ct).ConfigureAwait(false); }
                catch { break; }
                if (frame is null) break; // peer EOF -> end session

                LastActivityTick = Environment.TickCount64;
                try { await to.SendFrameAsync(frame, CancellationToken.None).ConfigureAwait(false); }
                catch { break; }
            }
            DiagLog.Log($"Server: relay pump {tag} ended (session {_id})");
        }
    }
}
