using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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
    [SerializeField] private string accountId = "player-local";
    [SerializeField] private int requestedZoneId = 1;
    [SerializeField] private bool connectOnStart = true;

    public Guid SessionId { get; private set; }
    public ulong PlayerId { get; private set; }
    public int CurrentZoneId { get; private set; }
    public Vector3 AuthoritativePosition { get; private set; }
    public string StatusText { get; private set; }
    public event Action<WorldSnapshotMessage> SnapshotReceived;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
    private readonly SemaphoreSlim _connectionSwapLock = new SemaphoreSlim(1, 1);
    private CancellationTokenSource _sessionCancellation;
    private ZoneConnection _activeConnection;
    private uint _inputSequence;
    private uint _gameplayCommandSequence;

    private async void Start()
    {
        StatusText = "Idle";
        if (connectOnStart)
        {
            await ConnectAsync();
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

    public async Task ConnectAsync()
    {
        await DisconnectAsync();
        _sessionCancellation = new CancellationTokenSource();
        StatusText = "Connecting to gateway";

        using (var gatewayClient = new TcpClient())
        {
            await gatewayClient.ConnectAsync(gatewayHost, gatewayTcpPort);
            using (var stream = gatewayClient.GetStream())
            {
                await WireProtocol.WriteTcpMessageAsync(
                    stream,
                    new ClientHelloMessage(WireProtocol.CurrentProtocolVersion, accountId, requestedZoneId),
                    _sessionCancellation.Token).ConfigureAwait(false);

                var response = await WireProtocol.ReadTcpMessageAsync(stream, _sessionCancellation.Token).ConfigureAwait(false);
                var accepted = response as HelloAcceptedMessage;
                if (accepted == null)
                {
                    throw new InvalidOperationException("Gateway did not return HelloAccepted.");
                }

                SessionId = accepted.SessionId;
                PlayerId = accepted.PlayerId;
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

                if (message is ErrorMessage error)
                {
                    _mainThreadActions.Enqueue(() => StatusText = error.Text);
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
                        Debug.Log("Received snapshot tick " + snapshot.Tick + " for zone " + snapshot.ZoneId + ".");
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
                        if (SnapshotReceived != null)
                        {
                            SnapshotReceived(deliveredSnapshot);
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
}
}
