using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class GatewayHost
{
    private readonly AccountStore _accountStore;
    private readonly ZoneDirectory _zoneDirectory;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ZoneSupervisor _zoneSupervisor;
    private readonly ModerationStore _moderationStore;
    private readonly GameplayDefinitionStore _gameplayDefinitions;
    private readonly TcpListener _listener;
    private long _connectionAttempts;
    private long _successfulLogins;
    private long _errors;
    private readonly int _port;

    public GatewayHost(AccountStore accountStore, ZoneDirectory zoneDirectory, SessionRegistry sessionRegistry, ZoneSupervisor zoneSupervisor, ModerationStore moderationStore, GameplayDefinitionStore gameplayDefinitions, int port)
    {
        _accountStore = accountStore;
        _zoneDirectory = zoneDirectory;
        _sessionRegistry = sessionRegistry;
        _zoneSupervisor = zoneSupervisor;
        _moderationStore = moderationStore;
        _gameplayDefinitions = gameplayDefinitions;
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
                if (message is AccountServiceRequestMessage accountServiceRequest)
                {
                    await HandleAccountServiceRequestAsync(stream, accountServiceRequest, cancellationToken).ConfigureAwait(false);
                    return;
                }

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

                AccountAuthResult authResult;
                try
                {
                    authResult = _accountStore.Authenticate(hello.AccountId, hello.Password, hello.AuthMode);
                }
                catch (InvalidOperationException ex)
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage(ex.Message), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!authResult.Success)
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage(authResult.ErrorText), cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_moderationStore.IsAccountBanned(authResult.AccountId))
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Account is banned."), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var zone = _zoneDirectory.GetZone(hello.RequestedZoneId == 0 ? 1 : hello.RequestedZoneId);
                try
                {
                    await _zoneSupervisor.EnsureRunningAsync(zone.ZoneId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await WireProtocol.WriteTcpMessageAsync(stream, new ErrorMessage("Zone startup failed: " + ex.Message), cancellationToken).ConfigureAwait(false);
                    return;
                }
                var spawn = _gameplayDefinitions.GetPreferredPlayerSpawn(zone.ZoneId, zone);
                var session = _sessionRegistry.CreateSession(authResult.AccountId, authResult.AccountName, authResult.CharacterId, zone.ZoneId, spawn);
                var token = _sessionRegistry.IssueTransferToken(session.SessionId, zone.ZoneId, spawn);

                var accepted = new HelloAcceptedMessage(
                    session.SessionId,
                    session.PlayerId,
                    zone.ZoneId,
                    zone.Host,
                    zone.TcpPort,
                    zone.UdpPort,
                    token,
                    zone.AssetBundleName,
                    SnapshotRateHz: 20);

                await WireProtocol.WriteTcpMessageAsync(stream, accepted, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _successfulLogins);
                Console.WriteLine($"Gateway authenticated account {authResult.AccountName} ({authResult.AccountId}) and assigned character {session.PlayerId} to zone {zone.ZoneId}.");
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

    private async Task HandleAccountServiceRequestAsync(NetworkStream stream, AccountServiceRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = request.ServiceKind switch
            {
                AccountServiceKind.CharacterList => new AccountServiceResponseMessage(
                    request.RequestId,
                    request.ServiceKind,
                    true,
                    JsonSerializer.Serialize(_accountStore.GetCharacterList(request.AccountName, request.Password), JsonOptions),
                    string.Empty),
                AccountServiceKind.CharacterCreate => HandleCreateCharacter(request),
                AccountServiceKind.CharacterSelect => HandleSelectCharacter(request),
                _ => new AccountServiceResponseMessage(request.RequestId, request.ServiceKind, false, string.Empty, "Unsupported account service request.")
            };

            await WireProtocol.WriteTcpMessageAsync(stream, response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WireProtocol.WriteTcpMessageAsync(
                stream,
                new AccountServiceResponseMessage(request.RequestId, request.ServiceKind, false, string.Empty, ex.Message),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private AccountServiceResponseMessage HandleCreateCharacter(AccountServiceRequestMessage request)
    {
        var payload = JsonSerializer.Deserialize<CharacterCreatePayload>(request.PayloadJson, JsonOptions) ?? new CharacterCreatePayload();
        var result = _accountStore.CreateCharacter(request.AccountName, request.Password, payload.CharacterName ?? string.Empty);
        return new AccountServiceResponseMessage(request.RequestId, request.ServiceKind, true, JsonSerializer.Serialize(result, JsonOptions), string.Empty);
    }

    private AccountServiceResponseMessage HandleSelectCharacter(AccountServiceRequestMessage request)
    {
        var payload = JsonSerializer.Deserialize<CharacterSelectPayload>(request.PayloadJson, JsonOptions) ?? new CharacterSelectPayload();
        var result = _accountStore.SelectCharacter(request.AccountName, request.Password, payload.CharacterId);
        return new AccountServiceResponseMessage(request.RequestId, request.ServiceKind, true, JsonSerializer.Serialize(result, JsonOptions), string.Empty);
    }

    private sealed class CharacterCreatePayload
    {
        public string CharacterName { get; set; } = string.Empty;
    }

    private sealed class CharacterSelectPayload
    {
        public ulong CharacterId { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
