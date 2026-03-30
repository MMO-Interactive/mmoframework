using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class GatewayHost
{
    private readonly ZoneDirectory _zoneDirectory;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ZoneSupervisor _zoneSupervisor;
    private readonly TcpListener _listener;
    private long _connectionAttempts;
    private long _successfulLogins;
    private long _errors;
    private readonly int _port;

    public GatewayHost(ZoneDirectory zoneDirectory, SessionRegistry sessionRegistry, ZoneSupervisor zoneSupervisor, int port)
    {
        _zoneDirectory = zoneDirectory;
        _sessionRegistry = sessionRegistry;
        _zoneSupervisor = zoneSupervisor;
        _port = port;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public GatewaySnapshot CreateDashboardSnapshot()
        => new(
            _port,
            Interlocked.Read(ref _connectionAttempts),
            Interlocked.Read(ref _successfulLogins),
            Interlocked.Read(ref _errors));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Console.WriteLine($"Gateway listening on TCP {_listener.LocalEndpoint}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _connectionAttempts);
        try
        {
            using (tcpClient)
            await using (var stream = tcpClient.GetStream())
            {
                var message = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (message is not ClientHelloMessage hello)
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Expected ClientHello."), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (hello.ProtocolVersion != WireProtocol.CurrentProtocolVersion)
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Protocol mismatch."), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var zone = _zoneDirectory.GetZone(hello.RequestedZoneId == 0 ? 1 : hello.RequestedZoneId);
                await _zoneSupervisor.EnsureRunningAsync(zone.ZoneId, cancellationToken).ConfigureAwait(false);
                var spawn = new NetworkVector3(zone.MinX + 5f, 0f, zone.MinZ + 5f);
                var session = _sessionRegistry.CreateSession(hello.AccountId, zone.ZoneId, spawn);
                var token = _sessionRegistry.IssueTransferToken(session.SessionId, zone.ZoneId, spawn);

                var accepted = new HelloAcceptedMessage(
                    session.SessionId,
                    session.PlayerId,
                    zone.ZoneId,
                    zone.Host,
                    zone.TcpPort,
                    zone.UdpPort,
                    token,
                    SnapshotRateHz: 20);

                await WireProtocol.WriteTcpMessageAsync(stream, accepted, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _successfulLogins);
                Console.WriteLine($"Gateway assigned player {session.PlayerId} to zone {zone.ZoneId}.");
            }
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errors);
            Console.WriteLine($"Gateway client error: {ex}");
        }
    }
}
