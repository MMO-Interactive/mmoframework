using System;

namespace MMONetworking;

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

public abstract record TcpMessage(TcpMessageKind Kind);

public abstract record UdpMessage(UdpMessageKind Kind);

public sealed record ClientHelloMessage(int ProtocolVersion, string AccountId, int RequestedZoneId)
    : TcpMessage(TcpMessageKind.ClientHello);

public sealed record HelloAcceptedMessage(
    Guid SessionId,
    ulong PlayerId,
    int ZoneId,
    string ZoneHost,
    int ZoneTcpPort,
    int ZoneUdpPort,
    string TransferToken,
    int SnapshotRateHz)
    : TcpMessage(TcpMessageKind.HelloAccepted);

public sealed record AttachToZoneMessage(Guid SessionId, ulong PlayerId, string TransferToken)
    : TcpMessage(TcpMessageKind.AttachToZone);

public sealed record AttachAcceptedMessage(Guid SessionId, int ZoneId, NetworkVector3 SpawnPosition)
    : TcpMessage(TcpMessageKind.AttachAccepted);

public sealed record ZoneTransferPrepareMessage(
    Guid SessionId,
    Guid TransferId,
    int FromZoneId,
    int ToZoneId,
    string ZoneHost,
    int ZoneTcpPort,
    int ZoneUdpPort,
    string TransferToken,
    NetworkVector3 SpawnPosition)
    : TcpMessage(TcpMessageKind.ZoneTransferPrepare);

public sealed record ZoneTransferCommittedMessage(Guid SessionId, Guid TransferId, int ZoneId)
    : TcpMessage(TcpMessageKind.ZoneTransferCommitted);

public sealed record HeartbeatMessage(long ServerTicks)
    : TcpMessage(TcpMessageKind.Heartbeat);

public sealed record ErrorMessage(string Text)
    : TcpMessage(TcpMessageKind.Error);

public sealed record ZoneAttachAuthorizeMessage(Guid SessionId, ulong PlayerId, int ZoneId, string TransferToken)
    : TcpMessage(TcpMessageKind.ZoneAttachAuthorize);

public sealed record ZoneAttachAuthorizedMessage(bool Success, Guid SessionId, ulong PlayerId, int ZoneId, NetworkVector3 SpawnPosition, string ErrorText)
    : TcpMessage(TcpMessageKind.ZoneAttachAuthorized);

public sealed record ZoneStateUpdateMessage(Guid SessionId, int ZoneId, NetworkVector3 Position)
    : TcpMessage(TcpMessageKind.ZoneStateUpdate);

public sealed record ZoneTransferRequestMessage(Guid SessionId, int ZoneId, NetworkVector3 Position)
    : TcpMessage(TcpMessageKind.ZoneTransferRequest);

public sealed record ZoneTransferResponseMessage(
    bool ShouldTransfer,
    Guid SessionId,
    int FromZoneId,
    int ToZoneId,
    string ZoneHost,
    int ZoneTcpPort,
    int ZoneUdpPort,
    string TransferToken,
    NetworkVector3 SpawnPosition,
    string ErrorText)
    : TcpMessage(TcpMessageKind.ZoneTransferResponse);

public sealed record ZonePrewarmRequestMessage(Guid SessionId, int ZoneId, NetworkVector3 Position)
    : TcpMessage(TcpMessageKind.ZonePrewarmRequest);

public sealed record ZonePrewarmResponseMessage(bool Started, Guid SessionId, int ZoneId, int DestinationZoneId, string ErrorText)
    : TcpMessage(TcpMessageKind.ZonePrewarmResponse);

public sealed record ClientInputMessage(Guid SessionId, uint Sequence, NetworkVector3 Move, float DeltaTimeSeconds)
    : UdpMessage(UdpMessageKind.ClientInput);

public sealed record WorldSnapshotMessage(int ZoneId, uint Tick, PlayerSnapshot[] Players)
    : UdpMessage(UdpMessageKind.WorldSnapshot);

public sealed record TransferProbeMessage(Guid SessionId, string TransferToken)
    : UdpMessage(UdpMessageKind.TransferProbe);

public sealed record TransferReadyMessage(Guid SessionId, int ZoneId)
    : UdpMessage(UdpMessageKind.TransferReady);
