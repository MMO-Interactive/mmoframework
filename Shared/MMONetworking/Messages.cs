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
    DisconnectNotice = 9,
    AccountServiceRequest = 10,
    AccountServiceResponse = 11,
    ZoneAttachAuthorize = 100,
    ZoneAttachAuthorized = 101,
    ZoneStateUpdate = 102,
    ZoneTransferRequest = 103,
    ZoneTransferResponse = 104,
    ZonePrewarmRequest = 105,
    ZonePrewarmResponse = 106,
    ZoneMobStateUpdate = 107,
    ZoneStateBatchUpdate = 108,
    ZoneGameplayReward = 109,
    GameplayCommand = 200,
    GameplayResult = 201,
    GameplayServiceRequest = 202,
    GameplayServiceResponse = 203,
    GameplayStatePush = 204
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

public enum AccountAuthMode : byte
{
    Login = 1,
    Register = 2
}

public enum AccountServiceKind : byte
{
    CharacterList = 1,
    CharacterCreate = 2,
    CharacterSelect = 3
}

public sealed record ClientHelloMessage(int ProtocolVersion, string AccountId, string Password, AccountAuthMode AuthMode, int RequestedZoneId)
    : TcpMessage(TcpMessageKind.ClientHello);

public sealed record HelloAcceptedMessage(
    Guid SessionId,
    ulong PlayerId,
    int ZoneId,
    string ZoneHost,
    int ZoneTcpPort,
    int ZoneUdpPort,
    string TransferToken,
    string ZoneBundleName,
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
    string ZoneBundleName,
    NetworkVector3 SpawnPosition)
    : TcpMessage(TcpMessageKind.ZoneTransferPrepare);

public sealed record ZoneTransferCommittedMessage(Guid SessionId, Guid TransferId, int ZoneId)
    : TcpMessage(TcpMessageKind.ZoneTransferCommitted);

public sealed record HeartbeatMessage(long ServerTicks)
    : TcpMessage(TcpMessageKind.Heartbeat);

public sealed record ErrorMessage(string Text)
    : TcpMessage(TcpMessageKind.Error);

public sealed record DisconnectNoticeMessage(string Reason, bool CanReconnect, int GraceSeconds)
    : TcpMessage(TcpMessageKind.DisconnectNotice);

public sealed record AccountServiceRequestMessage(
    uint RequestId,
    AccountServiceKind ServiceKind,
    string AccountName,
    string Password,
    string PayloadJson)
    : TcpMessage(TcpMessageKind.AccountServiceRequest);

public sealed record AccountServiceResponseMessage(
    uint RequestId,
    AccountServiceKind ServiceKind,
    bool Success,
    string PayloadJson,
    string ErrorText)
    : TcpMessage(TcpMessageKind.AccountServiceResponse);

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
    string ZoneBundleName,
    NetworkVector3 SpawnPosition,
    string ErrorText)
    : TcpMessage(TcpMessageKind.ZoneTransferResponse);

public sealed record ZonePrewarmRequestMessage(Guid SessionId, int ZoneId, NetworkVector3 Position)
    : TcpMessage(TcpMessageKind.ZonePrewarmRequest);

public sealed record ZonePrewarmResponseMessage(bool Started, Guid SessionId, int ZoneId, int DestinationZoneId, string ErrorText)
    : TcpMessage(TcpMessageKind.ZonePrewarmResponse);

public sealed record ZoneMobStateUpdateMessage(int ZoneId, MobSnapshot[] Mobs)
    : TcpMessage(TcpMessageKind.ZoneMobStateUpdate);

public sealed record ZoneStateBatchUpdateMessage(int ZoneId, ZonePlayerStateUpdate[] Players)
    : TcpMessage(TcpMessageKind.ZoneStateBatchUpdate);

public sealed record ZoneGameplayRewardMessage(
    Guid SessionId,
    string ItemId,
    int ItemQuantity,
    int MaxStack,
    string SkillTrackId,
    int SkillExperience)
    : TcpMessage(TcpMessageKind.ZoneGameplayReward);

public enum GameplayCommandKind : byte
{
    Gather = 1,
    InspectInventory = 2,
    CastSpell = 3,
    Farming = 4,
    Tame = 5,
    Craft = 6,
    Attack = 7
}

public enum GameplayServiceKind : byte
{
    Inventory = 1,
    CraftingRecipes = 2,
    CraftRecipe = 3,
    Progression = 4,
    CombatSnapshot = 5,
    CombatEnsure = 6,
    CombatAttack = 7,
    Reputation = 8,
    QuestBoard = 9,
    QuestAction = 10,
    QuestClaim = 11,
    WorldEvents = 12,
    WorldEventContribute = 13,
    WorldEventClaim = 14,
    Invasions = 15,
    InvasionRecordKill = 16,
    InvasionClaim = 17,
    FullState = 18,
    ShopCatalog = 19,
    ShopPurchase = 20,
    NpcContext = 21
}

public sealed record GameplayCommandMessage(
    Guid SessionId,
    uint CommandId,
    GameplayCommandKind CommandKind,
    string TargetId)
    : TcpMessage(TcpMessageKind.GameplayCommand);

public sealed record GameplayResultMessage(
    Guid SessionId,
    uint CommandId,
    bool Success,
    string Text,
    string ItemId,
    int ItemCount,
    int SkillValue)
    : TcpMessage(TcpMessageKind.GameplayResult);

public sealed record GameplayServiceRequestMessage(
    Guid SessionId,
    uint RequestId,
    GameplayServiceKind ServiceKind,
    string PayloadJson)
    : TcpMessage(TcpMessageKind.GameplayServiceRequest);

public sealed record GameplayServiceResponseMessage(
    Guid SessionId,
    uint RequestId,
    GameplayServiceKind ServiceKind,
    bool Success,
    string PayloadJson,
    string ErrorText)
    : TcpMessage(TcpMessageKind.GameplayServiceResponse);

public sealed record GameplayStatePushMessage(
    Guid SessionId,
    string PayloadJson)
    : TcpMessage(TcpMessageKind.GameplayStatePush);

public sealed record ClientInputMessage(Guid SessionId, uint Sequence, NetworkVector3 Move, float DeltaTimeSeconds)
    : UdpMessage(UdpMessageKind.ClientInput);

public sealed record WorldSnapshotMessage(int ZoneId, uint Tick, PlayerSnapshot[] Players, ResourceNodeSnapshot[] ResourceNodes, MobSnapshot[] Mobs, NpcSnapshot[] Npcs)
    : UdpMessage(UdpMessageKind.WorldSnapshot);

public sealed record TransferProbeMessage(Guid SessionId, string TransferToken)
    : UdpMessage(UdpMessageKind.TransferProbe);

public sealed record TransferReadyMessage(Guid SessionId, int ZoneId)
    : UdpMessage(UdpMessageKind.TransferReady);
