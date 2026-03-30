using System;

namespace MMONetworking
{
public enum TcpMessageKind : ushort
{
    ClientHello = 1,
    HelloAccepted = 2,
    AttachToZone = 3,
    AttachAccepted = 4,
    ZoneTransferPrepare = 5,
    ZoneTransferCommitted = 6,
    Heartbeat = 7,
    Error = 8,
    ZoneAttachAuthorize = 100,
    ZoneAttachAuthorized = 101,
    ZoneStateUpdate = 102,
    ZoneTransferRequest = 103,
    ZoneTransferResponse = 104,
    ZonePrewarmRequest = 105,
    ZonePrewarmResponse = 106
}

public enum UdpMessageKind : ushort
{
    ClientInput = 1,
    WorldSnapshot = 2,
    TransferProbe = 3,
    TransferReady = 4
}

public abstract class TcpMessage
{
    protected TcpMessage(TcpMessageKind kind) => Kind = kind;
    public TcpMessageKind Kind { get; }
}

public abstract class UdpMessage
{
    protected UdpMessage(UdpMessageKind kind) => Kind = kind;
    public UdpMessageKind Kind { get; }
}

public sealed class ClientHelloMessage : TcpMessage
{
    public ClientHelloMessage(int protocolVersion, string accountId, int requestedZoneId) : base(TcpMessageKind.ClientHello)
    {
        ProtocolVersion = protocolVersion;
        AccountId = accountId;
        RequestedZoneId = requestedZoneId;
    }

    public int ProtocolVersion { get; }
    public string AccountId { get; }
    public int RequestedZoneId { get; }
}

public sealed class HelloAcceptedMessage : TcpMessage
{
    public HelloAcceptedMessage(Guid sessionId, ulong playerId, int zoneId, string zoneHost, int zoneTcpPort, int zoneUdpPort, string transferToken, int snapshotRateHz)
        : base(TcpMessageKind.HelloAccepted)
    {
        SessionId = sessionId;
        PlayerId = playerId;
        ZoneId = zoneId;
        ZoneHost = zoneHost;
        ZoneTcpPort = zoneTcpPort;
        ZoneUdpPort = zoneUdpPort;
        TransferToken = transferToken;
        SnapshotRateHz = snapshotRateHz;
    }

    public Guid SessionId { get; }
    public ulong PlayerId { get; }
    public int ZoneId { get; }
    public string ZoneHost { get; }
    public int ZoneTcpPort { get; }
    public int ZoneUdpPort { get; }
    public string TransferToken { get; }
    public int SnapshotRateHz { get; }
}

public sealed class AttachToZoneMessage : TcpMessage
{
    public AttachToZoneMessage(Guid sessionId, ulong playerId, string transferToken) : base(TcpMessageKind.AttachToZone)
    {
        SessionId = sessionId;
        PlayerId = playerId;
        TransferToken = transferToken;
    }

    public Guid SessionId { get; }
    public ulong PlayerId { get; }
    public string TransferToken { get; }
}

public sealed class AttachAcceptedMessage : TcpMessage
{
    public AttachAcceptedMessage(Guid sessionId, int zoneId, NetworkVector3 spawnPosition) : base(TcpMessageKind.AttachAccepted)
    {
        SessionId = sessionId;
        ZoneId = zoneId;
        SpawnPosition = spawnPosition;
    }

    public Guid SessionId { get; }
    public int ZoneId { get; }
    public NetworkVector3 SpawnPosition { get; }
}

public sealed class ZoneTransferPrepareMessage : TcpMessage
{
    public ZoneTransferPrepareMessage(Guid sessionId, Guid transferId, int fromZoneId, int toZoneId, string zoneHost, int zoneTcpPort, int zoneUdpPort, string transferToken, NetworkVector3 spawnPosition)
        : base(TcpMessageKind.ZoneTransferPrepare)
    {
        SessionId = sessionId;
        TransferId = transferId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
        ZoneHost = zoneHost;
        ZoneTcpPort = zoneTcpPort;
        ZoneUdpPort = zoneUdpPort;
        TransferToken = transferToken;
        SpawnPosition = spawnPosition;
    }

    public Guid SessionId { get; }
    public Guid TransferId { get; }
    public int FromZoneId { get; }
    public int ToZoneId { get; }
    public string ZoneHost { get; }
    public int ZoneTcpPort { get; }
    public int ZoneUdpPort { get; }
    public string TransferToken { get; }
    public NetworkVector3 SpawnPosition { get; }
}

public sealed class ZoneTransferCommittedMessage : TcpMessage
{
    public ZoneTransferCommittedMessage(Guid sessionId, Guid transferId, int zoneId) : base(TcpMessageKind.ZoneTransferCommitted)
    {
        SessionId = sessionId;
        TransferId = transferId;
        ZoneId = zoneId;
    }

    public Guid SessionId { get; }
    public Guid TransferId { get; }
    public int ZoneId { get; }
}

public sealed class HeartbeatMessage : TcpMessage
{
    public HeartbeatMessage(long serverTicks) : base(TcpMessageKind.Heartbeat) => ServerTicks = serverTicks;
    public long ServerTicks { get; }
}

public sealed class ErrorMessage : TcpMessage
{
    public ErrorMessage(string text) : base(TcpMessageKind.Error) => Text = text;
    public string Text { get; }
}

public sealed class ZoneAttachAuthorizeMessage : TcpMessage
{
    public ZoneAttachAuthorizeMessage(Guid sessionId, ulong playerId, int zoneId, string transferToken) : base(TcpMessageKind.ZoneAttachAuthorize)
    {
        SessionId = sessionId;
        PlayerId = playerId;
        ZoneId = zoneId;
        TransferToken = transferToken;
    }

    public Guid SessionId { get; }
    public ulong PlayerId { get; }
    public int ZoneId { get; }
    public string TransferToken { get; }
}

public sealed class ZoneAttachAuthorizedMessage : TcpMessage
{
    public ZoneAttachAuthorizedMessage(bool success, Guid sessionId, ulong playerId, int zoneId, NetworkVector3 spawnPosition, string errorText) : base(TcpMessageKind.ZoneAttachAuthorized)
    {
        Success = success;
        SessionId = sessionId;
        PlayerId = playerId;
        ZoneId = zoneId;
        SpawnPosition = spawnPosition;
        ErrorText = errorText;
    }

    public bool Success { get; }
    public Guid SessionId { get; }
    public ulong PlayerId { get; }
    public int ZoneId { get; }
    public NetworkVector3 SpawnPosition { get; }
    public string ErrorText { get; }
}

public sealed class ZoneStateUpdateMessage : TcpMessage
{
    public ZoneStateUpdateMessage(Guid sessionId, int zoneId, NetworkVector3 position) : base(TcpMessageKind.ZoneStateUpdate)
    {
        SessionId = sessionId;
        ZoneId = zoneId;
        Position = position;
    }

    public Guid SessionId { get; }
    public int ZoneId { get; }
    public NetworkVector3 Position { get; }
}

public sealed class ZoneTransferRequestMessage : TcpMessage
{
    public ZoneTransferRequestMessage(Guid sessionId, int zoneId, NetworkVector3 position) : base(TcpMessageKind.ZoneTransferRequest)
    {
        SessionId = sessionId;
        ZoneId = zoneId;
        Position = position;
    }

    public Guid SessionId { get; }
    public int ZoneId { get; }
    public NetworkVector3 Position { get; }
}

public sealed class ZoneTransferResponseMessage : TcpMessage
{
    public ZoneTransferResponseMessage(bool shouldTransfer, Guid sessionId, int fromZoneId, int toZoneId, string zoneHost, int zoneTcpPort, int zoneUdpPort, string transferToken, NetworkVector3 spawnPosition, string errorText)
        : base(TcpMessageKind.ZoneTransferResponse)
    {
        ShouldTransfer = shouldTransfer;
        SessionId = sessionId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
        ZoneHost = zoneHost;
        ZoneTcpPort = zoneTcpPort;
        ZoneUdpPort = zoneUdpPort;
        TransferToken = transferToken;
        SpawnPosition = spawnPosition;
        ErrorText = errorText;
    }

    public bool ShouldTransfer { get; }
    public Guid SessionId { get; }
    public int FromZoneId { get; }
    public int ToZoneId { get; }
    public string ZoneHost { get; }
    public int ZoneTcpPort { get; }
    public int ZoneUdpPort { get; }
    public string TransferToken { get; }
    public NetworkVector3 SpawnPosition { get; }
    public string ErrorText { get; }
}

public sealed class ZonePrewarmRequestMessage : TcpMessage
{
    public ZonePrewarmRequestMessage(Guid sessionId, int zoneId, NetworkVector3 position) : base(TcpMessageKind.ZonePrewarmRequest)
    {
        SessionId = sessionId;
        ZoneId = zoneId;
        Position = position;
    }

    public Guid SessionId { get; }
    public int ZoneId { get; }
    public NetworkVector3 Position { get; }
}

public sealed class ZonePrewarmResponseMessage : TcpMessage
{
    public ZonePrewarmResponseMessage(bool started, Guid sessionId, int zoneId, int destinationZoneId, string errorText) : base(TcpMessageKind.ZonePrewarmResponse)
    {
        Started = started;
        SessionId = sessionId;
        ZoneId = zoneId;
        DestinationZoneId = destinationZoneId;
        ErrorText = errorText;
    }

    public bool Started { get; }
    public Guid SessionId { get; }
    public int ZoneId { get; }
    public int DestinationZoneId { get; }
    public string ErrorText { get; }
}

public sealed class ClientInputMessage : UdpMessage
{
    public ClientInputMessage(Guid sessionId, uint sequence, NetworkVector3 move, float deltaTimeSeconds) : base(UdpMessageKind.ClientInput)
    {
        SessionId = sessionId;
        Sequence = sequence;
        Move = move;
        DeltaTimeSeconds = deltaTimeSeconds;
    }

    public Guid SessionId { get; }
    public uint Sequence { get; }
    public NetworkVector3 Move { get; }
    public float DeltaTimeSeconds { get; }
}

public sealed class WorldSnapshotMessage : UdpMessage
{
    public WorldSnapshotMessage(int zoneId, uint tick, PlayerSnapshot[] players) : base(UdpMessageKind.WorldSnapshot)
    {
        ZoneId = zoneId;
        Tick = tick;
        Players = players;
    }

    public int ZoneId { get; }
    public uint Tick { get; }
    public PlayerSnapshot[] Players { get; }
}

public sealed class TransferProbeMessage : UdpMessage
{
    public TransferProbeMessage(Guid sessionId, string transferToken) : base(UdpMessageKind.TransferProbe)
    {
        SessionId = sessionId;
        TransferToken = transferToken;
    }

    public Guid SessionId { get; }
    public string TransferToken { get; }
}

public sealed class TransferReadyMessage : UdpMessage
{
    public TransferReadyMessage(Guid sessionId, int zoneId) : base(UdpMessageKind.TransferReady)
    {
        SessionId = sessionId;
        ZoneId = zoneId;
    }

    public Guid SessionId { get; }
    public int ZoneId { get; }
}
}
