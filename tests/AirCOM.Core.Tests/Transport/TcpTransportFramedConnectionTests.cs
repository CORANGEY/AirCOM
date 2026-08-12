using System.Net;
using System.Net.Sockets;
using AirCOM.Core.Protocol;
using AirCOM.Core.Transport;
using FluentAssertions;
using Xunit;

namespace AirCOM.Core.Tests.Transport;

/// <summary>
/// End-to-end TcpTransport + FramedConnection test: spin up a loopback TCP
/// listener, connect a client transport through FramedConnection on both ends,
/// and verify frames round-trip. This exercises the same byte-pump path the
/// bridge engine will use.
/// </summary>
public class TcpTransportFramedConnectionTests : IAsyncLifetime
{
    private TcpListener? _listener;
    private int _port;

    public async Task InitializeAsync()
    {
        // Bind to loopback on an ephemeral port.
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        await Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _listener?.Stop();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FramedConnection_RoundTripsDataFramesOverTcp()
    {
        // Server side: accept one connection, wrap in FramedConnection.
        var acceptTask = _listener!.AcceptTcpClientAsync();
        await using var serverClient = new TcpTransport();
        await serverClient.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));
        await using var serverTransport = new TcpTransport(await acceptTask);
        await using var serverFramed = new FramedConnection(serverTransport);
        serverFramed.StartReceivePump();

        await using var clientFramed = new FramedConnection(serverClient);
        clientFramed.StartReceivePump();

        // Send three frames client -> server.
        await clientFramed.SendDataAsync(new byte[] { 1, 2, 3 }, sessionId: 1, sequence: 1);
        await clientFramed.SendDataAsync(new byte[] { 4, 5 }, sessionId: 1, sequence: 2);
        await clientFramed.SendDataAsync(new byte[] { 7, 8, 9, 10 }, sessionId: 1, sequence: 3);

        var f1 = await serverFramed.ReadFrameAsync();
        var f2 = await serverFramed.ReadFrameAsync();
        var f3 = await serverFramed.ReadFrameAsync();

        f1.Should().NotBeNull();
        f1!.Payload.Should().Equal(new byte[] { 1, 2, 3 });
        f1.Sequence.Should().Be(1);

        f2!.Payload.Should().Equal(new byte[] { 4, 5 });

        f3!.Payload.Should().Equal(new byte[] { 7, 8, 9, 10 });
        f3.Sequence.Should().Be(3);

        // Echo back from server -> client to exercise the reverse path.
        await serverFramed.SendDataAsync(new byte[] { 0xAA, 0xBB }, sessionId: 1, sequence: 99);
        var echo = await clientFramed.ReadFrameAsync();
        echo!.Payload.Should().Equal(new byte[] { 0xAA, 0xBB });
        echo.Sequence.Should().Be(99);
    }

    [Fact]
    public async Task FramedConnection_StateTransitions_ReachConnected()
    {
        var acceptTask = _listener!.AcceptTcpClientAsync();
        await using var client = new TcpTransport();

        client.State.Should().Be(ConnectionState.Disconnected);

        await client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));
        (await acceptTask).Dispose();

        client.State.Should().Be(ConnectionState.Connected);
    }
}
