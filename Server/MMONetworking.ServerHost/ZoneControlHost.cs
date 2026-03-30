using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class ZoneControlHost
{
    private readonly ZoneDirectory _zoneDirectory;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ZoneSupervisor _zoneSupervisor;
    private readonly ZoneRuntimeSettings _settings;
    private readonly TcpListener _listener;
    private readonly int _port;

    public ZoneControlHost(ZoneDirectory zoneDirectory, SessionRegistry sessionRegistry, ZoneSupervisor zoneSupervisor, ZoneRuntimeSettings settings, int port)
    {
        _zoneDirectory = zoneDirectory;
        _sessionRegistry = sessionRegistry;
        _zoneSupervisor = zoneSupervisor;
        _settings = settings;
        _port = port;
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Console.WriteLine($"Zone control bridge listening on TCP {_port}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        try
        {
            using (tcpClient)
            await using (var stream = tcpClient.GetStream())
            {
                var message = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                switch (message)
                {
                    case ZoneAttachAuthorizeMessage attachAuthorize:
                        await HandleAttachAuthorizeAsync(stream, attachAuthorize, cancellationToken).ConfigureAwait(false);
                        return;
                    case ZoneStateUpdateMessage stateUpdate:
                        _sessionRegistry.SetCurrentZone(stateUpdate.SessionId, stateUpdate.ZoneId, stateUpdate.Position);
                        if (_sessionRegistry.TryGet(stateUpdate.SessionId, out var stateSession) && stateSession is not null)
                        {
                            _zoneSupervisor.ReportExternalPlayerState(stateUpdate.ZoneId, stateUpdate.SessionId, stateSession.PlayerId, stateUpdate.Position, NetworkVector3.Zero);
                        }
                        await WireProtocol.WriteTcpMessageAsync(stream, new HeartbeatMessage(Environment.TickCount64), cancellationToken).ConfigureAwait(false);
                        return;
                    case ZoneTransferRequestMessage transferRequest:
                        await HandleTransferRequestAsync(stream, transferRequest, cancellationToken).ConfigureAwait(false);
                        return;
                    case ZonePrewarmRequestMessage prewarmRequest:
                        await HandlePrewarmRequestAsync(stream, prewarmRequest, cancellationToken).ConfigureAwait(false);
                        return;
                    default:
                        await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Unsupported zone control message."), cancellationToken).ConfigureAwait(false);
                        return;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Zone control bridge error: {ex}");
        }
    }

    private async Task HandleAttachAuthorizeAsync(Stream stream, ZoneAttachAuthorizeMessage request, CancellationToken cancellationToken)
    {
        if (!_sessionRegistry.TryConsumeAttachment(request.SessionId, request.ZoneId, request.TransferToken, out var session, out var pending))
        {
            Console.WriteLine($"Zone control rejected attach for session {request.SessionId} into zone {request.ZoneId}.");
            await WireProtocol.WriteTcpMessageAsync(
                stream,
                new ZoneAttachAuthorizedMessage(false, request.SessionId, request.PlayerId, request.ZoneId, NetworkVector3.Zero, "Invalid transfer token."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        _sessionRegistry.SetCurrentZone(request.SessionId, request.ZoneId, pending.SpawnPosition);
        _zoneSupervisor.ReportExternalPlayerState(request.ZoneId, request.SessionId, session!.PlayerId, pending.SpawnPosition, NetworkVector3.Zero);
        Console.WriteLine($"Zone control authorized attach for session {request.SessionId} into zone {request.ZoneId} at {pending.SpawnPosition.X:F2},{pending.SpawnPosition.Z:F2}.");
        await WireProtocol.WriteTcpMessageAsync(
            stream,
            new ZoneAttachAuthorizedMessage(true, request.SessionId, session!.PlayerId, request.ZoneId, pending.SpawnPosition, string.Empty),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleTransferRequestAsync(Stream stream, ZoneTransferRequestMessage request, CancellationToken cancellationToken)
    {
        var currentZone = _zoneDirectory.GetZone(request.ZoneId);
        var destination = _zoneDirectory.ResolveDestination(request.Position, request.ZoneId);
        Console.WriteLine($"Zone control transfer request for session {request.SessionId} from zone {request.ZoneId} at {request.Position.X:F2},{request.Position.Z:F2}.");
        if (destination.ZoneId == request.ZoneId)
        {
            await WireProtocol.WriteTcpMessageAsync(
                stream,
                new ZoneTransferResponseMessage(false, request.SessionId, request.ZoneId, request.ZoneId, string.Empty, 0, 0, string.Empty, currentZone.Clamp(request.Position), string.Empty),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_sessionRegistry.TryGet(request.SessionId, out var session) || session is null)
        {
            await WireProtocol.WriteTcpMessageAsync(
                stream,
                new ZoneTransferResponseMessage(false, request.SessionId, request.ZoneId, request.ZoneId, string.Empty, 0, 0, string.Empty, request.Position, "Session not found."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await _zoneSupervisor.EnsureRunningAsync(destination.ZoneId, cancellationToken).ConfigureAwait(false);
        var spawn = CalculateTransferSpawn(currentZone, destination, request.Position);
        var token = _sessionRegistry.IssueTransferToken(request.SessionId, destination.ZoneId, spawn);
        _zoneSupervisor.RemoveExternalPlayer(request.ZoneId, request.SessionId);
        Console.WriteLine($"Zone control approved transfer for session {request.SessionId} from zone {request.ZoneId} to zone {destination.ZoneId}, spawn {spawn.X:F2},{spawn.Z:F2}.");

        await WireProtocol.WriteTcpMessageAsync(
            stream,
            new ZoneTransferResponseMessage(
                true,
                request.SessionId,
                request.ZoneId,
                destination.ZoneId,
                destination.Host,
                destination.TcpPort,
                destination.UdpPort,
                token,
                spawn,
                string.Empty),
            cancellationToken).ConfigureAwait(false);
    }

    private NetworkVector3 CalculateTransferSpawn(ZoneDefinition current, ZoneDefinition destination, NetworkVector3 currentPosition)
    {
        var spawn = destination.Clamp(currentPosition);
        if (destination.MinX >= current.MaxX)
        {
            return new NetworkVector3(destination.MinX + _settings.TransferInset, spawn.Y, spawn.Z);
        }

        if (destination.MaxX <= current.MinX)
        {
            return new NetworkVector3(destination.MaxX - _settings.TransferInset, spawn.Y, spawn.Z);
        }

        if (destination.MinZ >= current.MaxZ)
        {
            return new NetworkVector3(spawn.X, spawn.Y, destination.MinZ + _settings.TransferInset);
        }

        if (destination.MaxZ <= current.MinZ)
        {
            return new NetworkVector3(spawn.X, spawn.Y, destination.MaxZ - _settings.TransferInset);
        }

        return spawn;
    }

    private async Task HandlePrewarmRequestAsync(Stream stream, ZonePrewarmRequestMessage request, CancellationToken cancellationToken)
    {
        var destination = _zoneDirectory.GetPrewarmDestination(request.ZoneId, request.Position, _settings.PrewarmMargin);
        if (destination is null)
        {
            await WireProtocol.WriteTcpMessageAsync(
                stream,
                new ZonePrewarmResponseMessage(false, request.SessionId, request.ZoneId, request.ZoneId, string.Empty),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var resolvedDestination = destination.Value;
        await _zoneSupervisor.EnsureRunningAsync(resolvedDestination.ZoneId, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Zone control prewarmed zone {resolvedDestination.ZoneId} for session {request.SessionId} near zone {request.ZoneId} seam.");
        await WireProtocol.WriteTcpMessageAsync(
            stream,
            new ZonePrewarmResponseMessage(true, request.SessionId, request.ZoneId, resolvedDestination.ZoneId, string.Empty),
            cancellationToken).ConfigureAwait(false);
    }
}
