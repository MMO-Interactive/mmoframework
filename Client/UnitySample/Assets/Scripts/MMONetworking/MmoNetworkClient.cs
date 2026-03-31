using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MMONetworking.UnitySample;

public sealed class MmoNetworkClient : MonoBehaviour
{
    [Header("Gateway")]
    [SerializeField] private string gatewayHost = "127.0.0.1";
    [SerializeField] private int gatewayTcpPort = 7000;
    [SerializeField] private string accountId = "player-local";
    [SerializeField] private string password = "changeme123";
    [SerializeField] private AccountAuthMode authMode = AccountAuthMode.Login;
    [SerializeField] private int requestedZoneId = 1;
    [SerializeField] private bool connectOnStart = true;

    public event Action<WorldSnapshotMessage>? SnapshotReceived;
    public event Action<int>? ZoneChanged;

    public Guid SessionId { get; private set; }
    public ulong PlayerId { get; private set; }
    public int CurrentZoneId { get; private set; }
    public Vector3 AuthoritativePosition { get; private set; }

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly SemaphoreSlim _connectionSwapLock = new(1, 1);
    private CancellationTokenSource? _sessionCancellation;
    private ZoneConnection? _activeConnection;
    private uint _inputSequence;

    private async void Start()
    {
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
    }

    private void FixedUpdate()
    {
        if (_activeConnection is null)
        {
            return;
        }

        var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
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
        using var gatewayClient = new TcpClient();
        await gatewayClient.ConnectAsync(gatewayHost, gatewayTcpPort);
        await using var stream = gatewayClient.GetStream();

        await WireProtocol.WriteTcpMessageAsync(
            stream,
            new ClientHelloMessage(WireProtocol.CurrentProtocolVersion, accountId, password, authMode, requestedZoneId),
            _sessionCancellation.Token);

        var response = await WireProtocol.ReadTcpMessageAsync(stream, _sessionCancellation.Token);
        if (response is not HelloAcceptedMessage accepted)
        {
            throw new InvalidOperationException($"Gateway returned {response.GetType().Name} instead of HelloAccepted.");
        }

        SessionId = accepted.SessionId;
        PlayerId = accepted.PlayerId;
        await ReplaceZoneConnectionAsync(
            accepted.ZoneHost,
            accepted.ZoneTcpPort,
            accepted.ZoneUdpPort,
            accepted.TransferToken,
            CurrentZoneId,
            accepted.ZoneId,
            _sessionCancellation.Token);
    }

    public Task DisconnectAsync()
    {
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;

        var previous = Interlocked.Exchange(ref _activeConnection, null);
        previous?.Dispose();
        return Task.CompletedTask;
    }

    private async Task SendInputAsync(Vector3 move, float deltaTime)
    {
        var connection = _activeConnection;
        if (connection is null)
        {
            return;
        }

        var message = new ClientInputMessage(
            SessionId,
            ++_inputSequence,
            ToNetworkVector(move.normalized),
            deltaTime);

        var payload = WireProtocol.SerializeUdpMessage(message);
        try
        {
            await connection.UdpClient.SendAsync(payload, payload.Length);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task ReplaceZoneConnectionAsync(
        string zoneHost,
        int zoneTcpPort,
        int zoneUdpPort,
        string transferToken,
        int previousZoneId,
        int newZoneId,
        CancellationToken cancellationToken)
    {
        await _connectionSwapLock.WaitAsync(cancellationToken);
        try
        {
            var next = await CreateZoneConnectionAsync(zoneHost, zoneTcpPort, zoneUdpPort, transferToken, cancellationToken);
            var prior = Interlocked.Exchange(ref _activeConnection, next);
            prior?.Dispose();

            CurrentZoneId = newZoneId;
            AuthoritativePosition = next.SpawnPosition;
            StartConnectionLoops(next);

            _mainThreadActions.Enqueue(() => ZoneChanged?.Invoke(newZoneId));

            if (previousZoneId != 0 && previousZoneId != newZoneId)
            {
                Debug.Log($"Transferred from zone {previousZoneId} to zone {newZoneId}.");
            }
        }
        finally
        {
            _connectionSwapLock.Release();
        }
    }

    private async Task<ZoneConnection> CreateZoneConnectionAsync(
        string zoneHost,
        int zoneTcpPort,
        int zoneUdpPort,
        string transferToken,
        CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(zoneHost, zoneTcpPort);
        var stream = tcpClient.GetStream();

        await WireProtocol.WriteTcpMessageAsync(
            stream,
            new AttachToZoneMessage(SessionId, transferToken),
            cancellationToken);

        var attachResponse = await WireProtocol.ReadTcpMessageAsync(stream, cancellationToken);
        if (attachResponse is ErrorMessage error)
        {
            throw new InvalidOperationException(error.Text);
        }

        if (attachResponse is not AttachAcceptedMessage attached)
        {
            throw new InvalidOperationException($"Zone returned {attachResponse.GetType().Name} instead of AttachAccepted.");
        }

        var udpClient = new UdpClient(0);
        udpClient.Connect(zoneHost, zoneUdpPort);

        var probePayload = WireProtocol.SerializeUdpMessage(new TransferProbeMessage(SessionId, transferToken));
        await udpClient.SendAsync(probePayload, probePayload.Length);

        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return new ZoneConnection(tcpClient, stream, udpClient, linkedCancellation, attached.SpawnPosition);
    }

    private void StartConnectionLoops(ZoneConnection connection)
    {
        _ = Task.Run(() => ControlLoopAsync(connection), connection.Cancellation.Token);
        _ = Task.Run(() => UdpLoopAsync(connection), connection.Cancellation.Token);
    }

    private async Task ControlLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                var message = await WireProtocol.ReadTcpMessageAsync(connection.Stream, connection.Cancellation.Token);
                switch (message)
                {
                    case ZoneTransferPrepareMessage prepare when prepare.SessionId == SessionId:
                        await ReplaceZoneConnectionAsync(
                            prepare.ZoneHost,
                            prepare.ZoneTcpPort,
                            prepare.ZoneUdpPort,
                            prepare.TransferToken,
                            prepare.FromZoneId,
                            prepare.ToZoneId,
                            connection.Cancellation.Token);
                        return;
                    case ZoneTransferCommittedMessage committed when committed.SessionId == SessionId:
                        _mainThreadActions.Enqueue(() => CurrentZoneId = committed.ZoneId);
                        break;
                    case ErrorMessage error:
                        Debug.LogError(error.Text);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private async Task UdpLoopAsync(ZoneConnection connection)
    {
        try
        {
            while (!connection.Cancellation.IsCancellationRequested)
            {
                var result = await connection.UdpClient.ReceiveAsync(connection.Cancellation.Token);
                var message = WireProtocol.DeserializeUdpMessage(result.Buffer);
                switch (message)
                {
                    case TransferReadyMessage:
                        break;
                    case WorldSnapshotMessage snapshot when snapshot.ZoneId == CurrentZoneId:
                        foreach (var player in snapshot.Players)
                        {
                            if (player.PlayerId == PlayerId)
                            {
                                var nextPosition = ToUnityVector(player.Position);
                                _mainThreadActions.Enqueue(() => AuthoritativePosition = nextPosition);
                                break;
                            }
                        }

                        _mainThreadActions.Enqueue(() => SnapshotReceived?.Invoke(snapshot));
                        break;
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
        }
    }

    private static NetworkVector3 ToNetworkVector(Vector3 vector)
        => new(vector.x, vector.y, vector.z);

    private static Vector3 ToUnityVector(NetworkVector3 vector)
        => new(vector.X, vector.Y, vector.Z);

    private sealed class ZoneConnection : IDisposable
    {
        public ZoneConnection(
            TcpClient tcpClient,
            NetworkStream stream,
            UdpClient udpClient,
            CancellationTokenSource cancellation,
            NetworkVector3 spawnPosition)
        {
            TcpClient = tcpClient;
            Stream = stream;
            UdpClient = udpClient;
            Cancellation = cancellation;
            SpawnPosition = ToUnityVector(spawnPosition);
        }

        public TcpClient TcpClient { get; }
        public NetworkStream Stream { get; }
        public UdpClient UdpClient { get; }
        public CancellationTokenSource Cancellation { get; }
        public Vector3 SpawnPosition { get; }

        public void Dispose()
        {
            Cancellation.Cancel();
            Cancellation.Dispose();
            UdpClient.Dispose();
            Stream.Dispose();
            TcpClient.Dispose();
        }
    }
}
