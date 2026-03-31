using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoClient : MonoBehaviour
{
    [Header("Gateway")]
    [SerializeField] private string gatewayHost = "127.0.0.1";
    [SerializeField] private int gatewayTcpPort = 7000;
    [SerializeField] private int dashboardHttpPort = 7080;
    [SerializeField] private string accountId = "player-local";
    [SerializeField] private string password = "changeme123";
    [SerializeField] private AccountAuthMode authMode = AccountAuthMode.Login;
    [SerializeField] private int requestedZoneId = 1;
    [SerializeField] private bool connectOnStart = false;

    public Guid SessionId { get; private set; }
    public ulong PlayerId { get; private set; }
    public ulong SelectedCharacterId { get; private set; }
    public int CurrentZoneId { get; private set; }
    public Vector3 AuthoritativePosition { get; private set; }
    public string StatusText { get; private set; }
    public double LastHeartbeatRttMs { get; private set; }
    public double HeartbeatJitterMs { get; private set; }
    public CharacterOption[] Characters { get; private set; } = Array.Empty<CharacterOption>();
    public string AccountId
    {
        get => accountId;
        set => accountId = value ?? string.Empty;
    }

    public string Password
    {
        get => password;
        set => password = value ?? string.Empty;
    }

    public bool IsConnected => _activeConnection != null;
    public event Action<WorldSnapshotMessage> SnapshotReceived;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
    private readonly SemaphoreSlim _connectionSwapLock = new SemaphoreSlim(1, 1);
    private static readonly HttpClient SharedHttpClient = new HttpClient();
    private CancellationTokenSource _sessionCancellation;
    private ZoneConnection _activeConnection;
    private uint _inputSequence;
    private uint _gameplayCommandSequence;

    private async void Start()
    {
        StatusText = "Idle";
        if (connectOnStart)
        {
            await ConnectAsync(authMode);
        }
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }

        if (_activeConnection != null && Input.GetKeyDown(KeyCode.G))
        {
            _ = SendGatherCommandAsync("tree-1");
        }

        if (_activeConnection != null && Input.GetKeyDown(KeyCode.H))
        {
            _ = SendGatherCommandAsync("ore-1");
        }

        if (_activeConnection != null && Input.GetKeyDown(KeyCode.I))
        {
            _ = SendInspectInventoryAsync();
        }
    }

    private void FixedUpdate()
    {
        if (_activeConnection == null)
        {
            return;
        }

        var horizontal = 0f;
        var vertical = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            horizontal -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            horizontal += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            vertical -= 1f;
        }

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            vertical += 1f;
        }

        var move = new Vector3(horizontal, 0f, vertical);
        _ = SendInputAsync(move, Time.fixedDeltaTime);
    }

    private async void OnDestroy()
    {
        await DisconnectAsync();
    }

    public Task ConnectAsync()
        => ConnectAsync(authMode);

    public Task LoginAsync()
        => ConnectAsync(AccountAuthMode.Login);

    public Task RegisterAsync()
        => ConnectAsync(AccountAuthMode.Register);

    public async Task ConnectAsync(AccountAuthMode mode)
    {
        try
        {
            await DisconnectAsync();
            _sessionCancellation = new CancellationTokenSource();
            StatusText = mode == AccountAuthMode.Register
                ? "Registering account"
                : "Logging in";

            using (var gatewayClient = new TcpClient())
            {
                await gatewayClient.ConnectAsync(gatewayHost, gatewayTcpPort);
                using (var stream = gatewayClient.GetStream())
                {
                    await WireProtocol.WriteTcpMessageAsync(
                        stream,
                        new ClientHelloMessage(WireProtocol.CurrentProtocolVersion, accountId.Trim(), password, mode, requestedZoneId),
                        _sessionCancellation.Token).ConfigureAwait(false);

                    var response = await WireProtocol.ReadTcpMessageAsync(stream, _sessionCancellation.Token).ConfigureAwait(false);
                    if (response is ErrorMessage error)
                    {
                        throw new InvalidOperationException(error.Text);
                    }

                    var accepted = response as HelloAcceptedMessage;
                    if (accepted == null)
                    {
                        throw new InvalidOperationException("Gateway did not return HelloAccepted.");
                    }

                    accountId = accountId.Trim().ToLowerInvariant();
                    authMode = mode;
                    SessionId = accepted.SessionId;
                    PlayerId = accepted.PlayerId;
                    SelectedCharacterId = accepted.PlayerId;
                    await ReplaceZoneConnectionAsync(
                        accepted.ZoneHost,
                        accepted.ZoneTcpPort,
                        accepted.ZoneUdpPort,
                        accepted.TransferToken,
                        0,
                        accepted.ZoneId,
                        _sessionCancellation.Token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public Task DisconnectAsync()
    {
        if (_sessionCancellation != null)
        {
            _sessionCancellation.Cancel();
            _sessionCancellation.Dispose();
            _sessionCancellation = null;
        }

        var previous = Interlocked.Exchange(ref _activeConnection, null);
        if (previous != null)
        {
            previous.Dispose();
        }

        StatusText = "Disconnected";
        return Task.CompletedTask;
    }

    public async Task RefreshCharactersAsync()
    {
        try
        {
            var response = await PostCharacterApiAsync("/api/accounts/characters/list", new CharacterApiRequest
            {
                accountName = accountId.Trim(),
                password = password
            }).ConfigureAwait(false);

            ApplyCharacterResponse(response);
            StatusText = "Loaded " + Characters.Length + " characters.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task CreateCharacterAsync(string characterName)
    {
        try
        {
            var response = await PostCharacterApiAsync("/api/accounts/characters/create", new CharacterApiRequest
            {
                accountName = accountId.Trim(),
                password = password,
                characterName = characterName
            }).ConfigureAwait(false);

            ApplyCharacterResponse(response);
            StatusText = "Created character " + characterName + ".";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    public async Task SelectCharacterAsync(ulong characterId)
    {
        try
        {
            var response = await PostCharacterApiAsync("/api/accounts/characters/select", new CharacterApiRequest
            {
                accountName = accountId.Trim(),
                password = password,
                characterId = characterId
            }).ConfigureAwait(false);

            ApplyCharacterResponse(response);
            StatusText = "Selected character " + characterId + ".";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            Debug.LogException(ex);
        }
    }

    private async Task SendInputAsync(Vector3 move, float deltaTime)
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        var payload = WireProtocol.SerializeUdpMessage(
            new ClientInputMessage(SessionId, ++_inputSequence, ToNetworkVector(move.normalized), deltaTime));

        try
        {
            await connection.UdpClient.SendAsync(payload, payload.Length, connection.ZoneHost, connection.ZoneUdpPort).ConfigureAwait(false);
            if (_inputSequence % 20 == 0)
            {
                Debug.Log("Sent input seq " + _inputSequence + " to zone " + connection.ZoneUdpPort + ".");
            }
        }
        catch
        {
        }
    }

    private async Task ReplaceZoneConnectionAsync(string zoneHost, int zoneTcpPort, int zoneUdpPort, string transferToken, int previousZoneId, int newZoneId, CancellationToken cancellationToken)
    {
        await _connectionSwapLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessionToken = _sessionCancellation != null ? _sessionCancellation.Token : cancellationToken;
            var next = await CreateZoneConnectionAsync(zoneHost, zoneTcpPort, zoneUdpPort, transferToken, sessionToken).ConfigureAwait(false);
            var prior = Interlocked.Exchange(ref _activeConnection, next);
            StartConnectionLoops(next);
            if (prior != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000, sessionToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        prior.Dispose();
                    }
                }, CancellationToken.None);
            }

            CurrentZoneId = newZoneId;
            AuthoritativePosition = next.SpawnPosition;
            StatusText = "Connected to zone " + newZoneId + " (G tree / H ore / I inventory)";

            if (previousZoneId != 0 && previousZoneId != newZoneId)
            {
                Debug.Log("Transferred from zone " + previousZoneId + " to zone " + newZoneId + ".");
            }
        }
        finally
        {
            _connectionSwapLock.Release();
        }
    }

    private async Task<ZoneConnection> CreateZoneConnectionAsync(string zoneHost, int zoneTcpPort, int zoneUdpPort, string transferToken, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(zoneHost, zoneTcpPort);
        var stream = tcpClient.GetStream();

        await WireProtocol.WriteTcpMessageAsync(
            stream,
            new AttachToZoneMessage(SessionId, PlayerId, transferToken),
            cancellationToken).ConfigureAwait(false);

        var response = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken).ConfigureAwait(false);
        if (response is ErrorMessage error)
        {
            throw new InvalidOperationException(error.Text);
        }

        var attached = response as AttachAcceptedMessage;
        if (attached == null)
        {
            throw new InvalidOperationException("Zone did not return AttachAccepted.");
        }

        Debug.Log("TCP attached to zone " + attached.ZoneId + " at " + zoneHost + ":" + zoneUdpPort + ".");

        var udpClient = new UdpClient(0);
        var probePayload = WireProtocol.SerializeUdpMessage(new TransferProbeMessage(SessionId, transferToken));
        _ = SendTransferProbeAsync(udpClient, zoneHost, zoneUdpPort, probePayload);

        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return new ZoneConnection(tcpClient, stream, udpClient, linkedCancellation, ToUnityVector(attached.SpawnPosition), probePayload, zoneHost, zoneUdpPort);
    }

    private async Task SendTransferProbeAsync(UdpClient udpClient, string zoneHost, int zoneUdpPort, byte[] probePayload)
    {
        try
        {
            await udpClient.SendAsync(probePayload, probePayload.Length, zoneHost, zoneUdpPort).ConfigureAwait(false);
            Debug.Log("Sent transfer probe to " + zoneHost + ":" + zoneUdpPort + ".");
        }
        catch
        {
        }
    }

    private void StartConnectionLoops(ZoneConnection connection)
    {
        _ = Task.Run(() => ControlLoopAsync(connection), connection.Cancellation.Token);
        _ = Task.Run(() => UdpLoopAsync(connection), connection.Cancellation.Token);
        _ = Task.Run(() => ProbeLoopAsync(connection), connection.Cancellation.Token);
        _ = Task.Run(() => HeartbeatLoopAsync(connection), connection.Cancellation.Token);
    }

    private async Task SendGatherCommandAsync(string targetId)
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        try
        {
            var command = new GameplayCommandMessage(SessionId, ++_gameplayCommandSequence, GameplayCommandKind.Gather, targetId);
            await connection.SendControlMessageAsync(command).ConfigureAwait(false);
            _mainThreadActions.Enqueue(() => StatusText = "Gathering from " + targetId + "...");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task SendInspectInventoryAsync()
    {
        var connection = _activeConnection;
        if (connection == null)
        {
            return;
        }

        try
        {
            var command = new GameplayCommandMessage(SessionId, ++_gameplayCommandSequence, GameplayCommandKind.InspectInventory, string.Empty);
            await connection.SendControlMessageAsync(command).ConfigureAwait(false);
            _mainThreadActions.Enqueue(() => StatusText = "Requesting inventory...");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task ControlLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                var message = await WireProtocol.ReadTcpMessageAsync(connection.Stream, connection.Cancellation.Token).ConfigureAwait(false);
                if (message is ZoneTransferPrepareMessage prepare && prepare.SessionId == SessionId)
                {
                    _mainThreadActions.Enqueue(() => StatusText = "Transferring to zone " + prepare.ToZoneId);
                    await ReplaceZoneConnectionAsync(
                        prepare.ZoneHost,
                        prepare.ZoneTcpPort,
                        prepare.ZoneUdpPort,
                        prepare.TransferToken,
                        prepare.FromZoneId,
                        prepare.ToZoneId,
                        _sessionCancellation != null ? _sessionCancellation.Token : connection.Cancellation.Token).ConfigureAwait(false);
                    return;
                }

                if (message is HeartbeatMessage heartbeat)
                {
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var rttMs = Math.Max(0, nowMs - heartbeat.ServerTicks);
                    var previousRtt = LastHeartbeatRttMs;
                    _mainThreadActions.Enqueue(() =>
                    {
                        LastHeartbeatRttMs = rttMs;
                        if (previousRtt > 0)
                        {
                            HeartbeatJitterMs = Math.Abs(rttMs - previousRtt);
                        }
                    });
                    continue;
                }

                if (message is ErrorMessage error)
                {
                    _mainThreadActions.Enqueue(() => StatusText = error.Text);
                    continue;
                }

                if (message is DisconnectNoticeMessage disconnect)
                {
                    _mainThreadActions.Enqueue(() =>
                    {
                        StatusText = disconnect.Reason + (disconnect.CanReconnect ? " Reconnect grace: " + disconnect.GraceSeconds + "s." : string.Empty);
                    });
                    continue;
                }

                var gameplayResult = message as GameplayResultMessage;
                if (gameplayResult != null)
                {
                    _mainThreadActions.Enqueue(() =>
                        StatusText = gameplayResult.Text + " | " + gameplayResult.ItemId + ": " + gameplayResult.ItemCount + " | Gathering " + gameplayResult.SkillValue);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            _mainThreadActions.Enqueue(() => StatusText = "Control connection error");
        }
    }

    private async Task UdpLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                var result = await connection.UdpClient.ReceiveAsync().ConfigureAwait(false);
                var message = WireProtocol.DeserializeUdpMessage(result.Buffer);
                connection.HasSeenUdpTraffic = true;
                if (message is TransferReadyMessage ready)
                {
                    Debug.Log("Received TransferReady for zone " + ready.ZoneId + ".");
                }

                if (message is WorldSnapshotMessage snapshot && snapshot.ZoneId == CurrentZoneId)
                {
                    if (snapshot.Tick % 20 == 0)
                    {
                        Debug.Log("Received snapshot tick " + snapshot.Tick + " for zone " + snapshot.ZoneId + " with " + snapshot.ResourceNodes.Length + " resource nodes.");
                    }
                    for (var i = 0; i < snapshot.Players.Length; i++)
                    {
                        var player = snapshot.Players[i];
                        if (player.PlayerId == PlayerId)
                        {
                            var nextPosition = ToUnityVector(player.Position);
                            _mainThreadActions.Enqueue(() => AuthoritativePosition = nextPosition);
                            break;
                        }
                    }

                    var deliveredSnapshot = snapshot;
                    _mainThreadActions.Enqueue(() =>
                    {
                        var handlers = SnapshotReceived;
                        if (handlers != null)
                        {
                            var invocationList = handlers.GetInvocationList();
                            for (var i = 0; i < invocationList.Length; i++)
                            {
                                try
                                {
                                    ((Action<WorldSnapshotMessage>)invocationList[i])(deliveredSnapshot);
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogException(ex);
                                }
                            }
                        }
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            _mainThreadActions.Enqueue(() => StatusText = "UDP connection error");
        }
    }

    private async Task ProbeLoopAsync(ZoneConnection connection)
    {
        try
        {
            for (var attempt = 0; attempt < 15 && !connection.Cancellation.IsCancellationRequested; attempt++)
            {
                if (connection.HasSeenUdpTraffic)
                {
                    return;
                }

                await SendTransferProbeAsync(connection.UdpClient, connection.ZoneHost, connection.ZoneUdpPort, connection.ProbePayload).ConfigureAwait(false);
                await Task.Delay(200, connection.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static NetworkVector3 ToNetworkVector(Vector3 vector)
        => new NetworkVector3(vector.x, vector.y, vector.z);

    private static Vector3 ToUnityVector(NetworkVector3 vector)
        => new Vector3(vector.X, vector.Y, vector.Z);

    private void ApplyCharacterResponse(AccountCharacterListResponse response)
    {
        SelectedCharacterId = response.selectedCharacterId;
        Characters = response.characters ?? Array.Empty<CharacterOption>();
    }

    private async Task<AccountCharacterListResponse> PostCharacterApiAsync(string path, CharacterApiRequest payload)
    {
        var url = "http://" + gatewayHost + ":" + dashboardHttpPort + path;
        var json = JsonUtility.ToJson(payload);
        using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
        using (var response = await SharedHttpClient.PostAsync(url, content).ConfigureAwait(false))
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = JsonUtility.FromJson<ErrorEnvelope>(body);
                throw new InvalidOperationException(error != null && !string.IsNullOrWhiteSpace(error.error) ? error.error : "Character request failed.");
            }

            var parsed = JsonUtility.FromJson<AccountCharacterListResponse>(body);
            if (parsed == null)
            {
                throw new InvalidOperationException("Character service returned an empty response.");
            }

            return parsed;
        }
    }

    private async Task HeartbeatLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), connection.Cancellation.Token).ConfigureAwait(false);
                if (connection.Cancellation.IsCancellationRequested)
                {
                    return;
                }

                await connection.SendControlMessageAsync(new HeartbeatMessage(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private sealed class ZoneConnection : IDisposable
    {
        public ZoneConnection(TcpClient tcpClient, NetworkStream stream, UdpClient udpClient, CancellationTokenSource cancellation, Vector3 spawnPosition, byte[] probePayload, string zoneHost, int zoneUdpPort)
        {
            TcpClient = tcpClient;
            Stream = stream;
            UdpClient = udpClient;
            Cancellation = cancellation;
            SpawnPosition = spawnPosition;
            ProbePayload = probePayload;
            ZoneHost = zoneHost;
            ZoneUdpPort = zoneUdpPort;
        }

        public TcpClient TcpClient { get; }
        public NetworkStream Stream { get; }
        public UdpClient UdpClient { get; }
        public CancellationTokenSource Cancellation { get; }
        public Vector3 SpawnPosition { get; }
        public byte[] ProbePayload { get; }
        public string ZoneHost { get; }
        public int ZoneUdpPort { get; }
        public bool HasSeenUdpTraffic { get; set; }

        public async Task SendControlMessageAsync(TcpMessage message)
        {
            await _controlWriteLock.WaitAsync(Cancellation.Token).ConfigureAwait(false);
            try
            {
                await WireProtocol.WriteTcpMessageAsync(Stream, message, Cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                _controlWriteLock.Release();
            }
        }

        public void Dispose()
        {
            Cancellation.Cancel();
            Cancellation.Dispose();
            UdpClient.Dispose();
            Stream.Dispose();
            TcpClient.Dispose();
        }

        private readonly SemaphoreSlim _controlWriteLock = new SemaphoreSlim(1, 1);
    }

    [Serializable]
    private sealed class CharacterApiRequest
    {
        public string accountName;
        public string password;
        public string characterName;
        public ulong characterId;
    }

    [Serializable]
    public sealed class CharacterOption
    {
        public ulong characterId;
        public string characterName;
        public string createdAtUtc;
        public string lastSelectedAtUtc;
    }

    [Serializable]
    private sealed class AccountCharacterListResponse
    {
        public string accountId;
        public string accountName;
        public ulong selectedCharacterId;
        public CharacterOption[] characters;
    }

    [Serializable]
    private sealed class ErrorEnvelope
    {
        public string error;
    }
}
}
