using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MMONetworking;

public static class WireProtocol
{
    public const int CurrentProtocolVersion = 7;

    public static async Task WriteTcpMessageAsync(Stream stream, TcpMessage message, CancellationToken cancellationToken = default)
    {
        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)message.Kind);
            WriteTcpPayload(writer, message);
        }

        var payload = payloadStream.ToArray();
        var lengthBytes = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TcpMessage> ReadTcpMessageAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var lengthBytes = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var payloadLength = BitConverter.ToInt32(lengthBytes, 0);
        var payloadBytes = await ReadExactAsync(stream, payloadLength, cancellationToken).ConfigureAwait(false);
        using var payloadStream = new MemoryStream(payloadBytes, writable: false);
        using var reader = new BinaryReader(payloadStream, Encoding.UTF8, leaveOpen: true);
        var kind = (TcpMessageKind)reader.ReadUInt16();
        return ReadTcpPayload(reader, kind);
    }

    public static byte[] SerializeUdpMessage(UdpMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)message.Kind);
        WriteUdpPayload(writer, message);
        return stream.ToArray();
    }

    public static UdpMessage DeserializeUdpMessage(byte[] payload)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var kind = (UdpMessageKind)reader.ReadUInt16();
        return ReadUdpPayload(reader, kind);
    }

    private static void WriteTcpPayload(BinaryWriter writer, TcpMessage message)
    {
        switch (message)
        {
            case ClientHelloMessage hello:
                writer.Write(hello.ProtocolVersion);
                writer.Write(hello.AccountId);
                writer.Write(hello.Password);
                writer.Write((byte)hello.AuthMode);
                writer.Write(hello.RequestedZoneId);
                break;
            case HelloAcceptedMessage accepted:
                WriteGuid(writer, accepted.SessionId);
                writer.Write(accepted.PlayerId);
                writer.Write(accepted.ZoneId);
                writer.Write(accepted.ZoneHost);
                writer.Write(accepted.ZoneTcpPort);
                writer.Write(accepted.ZoneUdpPort);
                writer.Write(accepted.TransferToken);
                writer.Write(accepted.ZoneBundleName);
                writer.Write(accepted.SnapshotRateHz);
                break;
            case AttachToZoneMessage attach:
                WriteGuid(writer, attach.SessionId);
                writer.Write(attach.PlayerId);
                writer.Write(attach.TransferToken);
                break;
            case AttachAcceptedMessage attachAccepted:
                WriteGuid(writer, attachAccepted.SessionId);
                writer.Write(attachAccepted.ZoneId);
                WriteVector3(writer, attachAccepted.SpawnPosition);
                break;
            case ZoneTransferPrepareMessage transferPrepare:
                WriteGuid(writer, transferPrepare.SessionId);
                WriteGuid(writer, transferPrepare.TransferId);
                writer.Write(transferPrepare.FromZoneId);
                writer.Write(transferPrepare.ToZoneId);
                writer.Write(transferPrepare.ZoneHost);
                writer.Write(transferPrepare.ZoneTcpPort);
                writer.Write(transferPrepare.ZoneUdpPort);
                writer.Write(transferPrepare.TransferToken);
                writer.Write(transferPrepare.ZoneBundleName);
                WriteVector3(writer, transferPrepare.SpawnPosition);
                break;
            case ZoneTransferCommittedMessage transferCommitted:
                WriteGuid(writer, transferCommitted.SessionId);
                WriteGuid(writer, transferCommitted.TransferId);
                writer.Write(transferCommitted.ZoneId);
                break;
            case HeartbeatMessage heartbeat:
                writer.Write(heartbeat.ServerTicks);
                break;
            case ErrorMessage error:
                writer.Write(error.Text);
                break;
            case DisconnectNoticeMessage disconnect:
                writer.Write(disconnect.Reason);
                writer.Write(disconnect.CanReconnect);
                writer.Write(disconnect.GraceSeconds);
                break;
            case AccountServiceRequestMessage accountServiceRequest:
                writer.Write(accountServiceRequest.RequestId);
                writer.Write((byte)accountServiceRequest.ServiceKind);
                writer.Write(accountServiceRequest.AccountName);
                writer.Write(accountServiceRequest.Password);
                writer.Write(accountServiceRequest.PayloadJson);
                break;
            case AccountServiceResponseMessage accountServiceResponse:
                writer.Write(accountServiceResponse.RequestId);
                writer.Write((byte)accountServiceResponse.ServiceKind);
                writer.Write(accountServiceResponse.Success);
                writer.Write(accountServiceResponse.PayloadJson);
                writer.Write(accountServiceResponse.ErrorText);
                break;
            case ZoneAttachAuthorizeMessage authorize:
                WriteGuid(writer, authorize.SessionId);
                writer.Write(authorize.PlayerId);
                writer.Write(authorize.ZoneId);
                writer.Write(authorize.TransferToken);
                break;
            case ZoneAttachAuthorizedMessage authorized:
                writer.Write(authorized.Success);
                WriteGuid(writer, authorized.SessionId);
                writer.Write(authorized.PlayerId);
                writer.Write(authorized.ZoneId);
                WriteVector3(writer, authorized.SpawnPosition);
                writer.Write(authorized.ErrorText);
                break;
            case ZoneStateUpdateMessage stateUpdate:
                WriteGuid(writer, stateUpdate.SessionId);
                writer.Write(stateUpdate.ZoneId);
                WriteVector3(writer, stateUpdate.Position);
                break;
            case ZoneTransferRequestMessage transferRequest:
                WriteGuid(writer, transferRequest.SessionId);
                writer.Write(transferRequest.ZoneId);
                WriteVector3(writer, transferRequest.Position);
                break;
            case ZoneTransferResponseMessage transferResponse:
                writer.Write(transferResponse.ShouldTransfer);
                WriteGuid(writer, transferResponse.SessionId);
                writer.Write(transferResponse.FromZoneId);
                writer.Write(transferResponse.ToZoneId);
                writer.Write(transferResponse.ZoneHost);
                writer.Write(transferResponse.ZoneTcpPort);
                writer.Write(transferResponse.ZoneUdpPort);
                writer.Write(transferResponse.TransferToken);
                writer.Write(transferResponse.ZoneBundleName);
                WriteVector3(writer, transferResponse.SpawnPosition);
                writer.Write(transferResponse.ErrorText);
                break;
            case ZonePrewarmRequestMessage prewarmRequest:
                WriteGuid(writer, prewarmRequest.SessionId);
                writer.Write(prewarmRequest.ZoneId);
                WriteVector3(writer, prewarmRequest.Position);
                break;
            case ZonePrewarmResponseMessage prewarmResponse:
                writer.Write(prewarmResponse.Started);
                WriteGuid(writer, prewarmResponse.SessionId);
                writer.Write(prewarmResponse.ZoneId);
                writer.Write(prewarmResponse.DestinationZoneId);
                writer.Write(prewarmResponse.ErrorText);
                break;
            case ZoneMobStateUpdateMessage mobStateUpdate:
                writer.Write(mobStateUpdate.ZoneId);
                writer.Write(mobStateUpdate.Mobs.Length);
                foreach (var mob in mobStateUpdate.Mobs)
                {
                    writer.Write(mob.MobId);
                    writer.Write(mob.MobTypeId);
                    WriteVector3(writer, mob.Position);
                    WriteVector3(writer, mob.Velocity);
                    writer.Write(mob.State);
                }
                break;
            case ZoneStateBatchUpdateMessage stateBatch:
                writer.Write(stateBatch.ZoneId);
                writer.Write(stateBatch.Players.Length);
                foreach (var player in stateBatch.Players)
                {
                    WriteGuid(writer, player.SessionId);
                    writer.Write(player.PlayerId);
                    WriteVector3(writer, player.Position);
                    WriteVector3(writer, player.Velocity);
                }
                break;
            case ZoneGameplayRewardMessage gameplayReward:
                WriteGuid(writer, gameplayReward.SessionId);
                writer.Write(gameplayReward.ItemId);
                writer.Write(gameplayReward.ItemQuantity);
                writer.Write(gameplayReward.MaxStack);
                writer.Write(gameplayReward.SkillTrackId);
                writer.Write(gameplayReward.SkillExperience);
                break;
            case GameplayCommandMessage gameplayCommand:
                WriteGuid(writer, gameplayCommand.SessionId);
                writer.Write(gameplayCommand.CommandId);
                writer.Write((byte)gameplayCommand.CommandKind);
                writer.Write(gameplayCommand.TargetId);
                break;
            case GameplayResultMessage gameplayResult:
                WriteGuid(writer, gameplayResult.SessionId);
                writer.Write(gameplayResult.CommandId);
                writer.Write(gameplayResult.Success);
                writer.Write(gameplayResult.Text);
                writer.Write(gameplayResult.ItemId);
                writer.Write(gameplayResult.ItemCount);
                writer.Write(gameplayResult.SkillValue);
                break;
            case GameplayServiceRequestMessage gameplayServiceRequest:
                WriteGuid(writer, gameplayServiceRequest.SessionId);
                writer.Write(gameplayServiceRequest.RequestId);
                writer.Write((byte)gameplayServiceRequest.ServiceKind);
                writer.Write(gameplayServiceRequest.PayloadJson);
                break;
            case GameplayServiceResponseMessage gameplayServiceResponse:
                WriteGuid(writer, gameplayServiceResponse.SessionId);
                writer.Write(gameplayServiceResponse.RequestId);
                writer.Write((byte)gameplayServiceResponse.ServiceKind);
                writer.Write(gameplayServiceResponse.Success);
                writer.Write(gameplayServiceResponse.PayloadJson);
                writer.Write(gameplayServiceResponse.ErrorText);
                break;
            case GameplayStatePushMessage gameplayStatePush:
                WriteGuid(writer, gameplayStatePush.SessionId);
                writer.Write(gameplayStatePush.PayloadJson);
                break;
            default:
                throw new InvalidDataException($"Unsupported TCP message type {message.GetType().Name}.");
        }
    }

    private static TcpMessage ReadTcpPayload(BinaryReader reader, TcpMessageKind kind)
    {
        return kind switch
        {
            TcpMessageKind.ClientHello => new ClientHelloMessage(
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadString(),
                (AccountAuthMode)reader.ReadByte(),
                reader.ReadInt32()),
            TcpMessageKind.HelloAccepted => new HelloAcceptedMessage(
                ReadGuid(reader),
                reader.ReadUInt64(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadInt32()),
            TcpMessageKind.AttachToZone => new AttachToZoneMessage(
                ReadGuid(reader),
                reader.ReadUInt64(),
                reader.ReadString()),
            TcpMessageKind.AttachAccepted => new AttachAcceptedMessage(
                ReadGuid(reader),
                reader.ReadInt32(),
                ReadVector3(reader)),
            TcpMessageKind.ZoneTransferPrepare => new ZoneTransferPrepareMessage(
                ReadGuid(reader),
                ReadGuid(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadString(),
                ReadVector3(reader)),
            TcpMessageKind.ZoneTransferCommitted => new ZoneTransferCommittedMessage(
                ReadGuid(reader),
                ReadGuid(reader),
                reader.ReadInt32()),
            TcpMessageKind.Heartbeat => new HeartbeatMessage(reader.ReadInt64()),
            TcpMessageKind.Error => new ErrorMessage(reader.ReadString()),
            TcpMessageKind.DisconnectNotice => new DisconnectNoticeMessage(
                reader.ReadString(),
                reader.ReadBoolean(),
                reader.ReadInt32()),
            TcpMessageKind.AccountServiceRequest => new AccountServiceRequestMessage(
                reader.ReadUInt32(),
                (AccountServiceKind)reader.ReadByte(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString()),
            TcpMessageKind.AccountServiceResponse => new AccountServiceResponseMessage(
                reader.ReadUInt32(),
                (AccountServiceKind)reader.ReadByte(),
                reader.ReadBoolean(),
                reader.ReadString(),
                reader.ReadString()),
            TcpMessageKind.ZoneAttachAuthorize => new ZoneAttachAuthorizeMessage(
                ReadGuid(reader),
                reader.ReadUInt64(),
                reader.ReadInt32(),
                reader.ReadString()),
            TcpMessageKind.ZoneAttachAuthorized => new ZoneAttachAuthorizedMessage(
                reader.ReadBoolean(),
                ReadGuid(reader),
                reader.ReadUInt64(),
                reader.ReadInt32(),
                ReadVector3(reader),
                reader.ReadString()),
            TcpMessageKind.ZoneStateUpdate => new ZoneStateUpdateMessage(
                ReadGuid(reader),
                reader.ReadInt32(),
                ReadVector3(reader)),
            TcpMessageKind.ZoneTransferRequest => new ZoneTransferRequestMessage(
                ReadGuid(reader),
                reader.ReadInt32(),
                ReadVector3(reader)),
            TcpMessageKind.ZoneTransferResponse => new ZoneTransferResponseMessage(
                reader.ReadBoolean(),
                ReadGuid(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadString(),
                ReadVector3(reader),
                reader.ReadString()),
            TcpMessageKind.ZonePrewarmRequest => new ZonePrewarmRequestMessage(
                ReadGuid(reader),
                reader.ReadInt32(),
                ReadVector3(reader)),
            TcpMessageKind.ZonePrewarmResponse => new ZonePrewarmResponseMessage(
                reader.ReadBoolean(),
                ReadGuid(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString()),
            TcpMessageKind.ZoneMobStateUpdate => ReadZoneMobStateUpdate(reader),
            TcpMessageKind.ZoneStateBatchUpdate => ReadZoneStateBatchUpdate(reader),
            TcpMessageKind.ZoneGameplayReward => new ZoneGameplayRewardMessage(
                ReadGuid(reader),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32()),
            TcpMessageKind.GameplayCommand => new GameplayCommandMessage(
                ReadGuid(reader),
                reader.ReadUInt32(),
                (GameplayCommandKind)reader.ReadByte(),
                reader.ReadString()),
            TcpMessageKind.GameplayResult => new GameplayResultMessage(
                ReadGuid(reader),
                reader.ReadUInt32(),
                reader.ReadBoolean(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32()),
            TcpMessageKind.GameplayServiceRequest => new GameplayServiceRequestMessage(
                ReadGuid(reader),
                reader.ReadUInt32(),
                (GameplayServiceKind)reader.ReadByte(),
                reader.ReadString()),
            TcpMessageKind.GameplayServiceResponse => new GameplayServiceResponseMessage(
                ReadGuid(reader),
                reader.ReadUInt32(),
                (GameplayServiceKind)reader.ReadByte(),
                reader.ReadBoolean(),
                reader.ReadString(),
                reader.ReadString()),
            TcpMessageKind.GameplayStatePush => new GameplayStatePushMessage(
                ReadGuid(reader),
                reader.ReadString()),
            _ => throw new InvalidDataException($"Unsupported TCP message kind {kind}.")
        };
    }

    private static void WriteUdpPayload(BinaryWriter writer, UdpMessage message)
    {
        switch (message)
        {
            case ClientInputMessage input:
                WriteGuid(writer, input.SessionId);
                writer.Write(input.Sequence);
                WriteVector3(writer, input.Move);
                writer.Write(input.DeltaTimeSeconds);
                break;
            case WorldSnapshotMessage snapshot:
                writer.Write(snapshot.ZoneId);
                writer.Write(snapshot.Tick);
                writer.Write(snapshot.Players.Length);
                foreach (var player in snapshot.Players)
                {
                    writer.Write(player.PlayerId);
                    WriteVector3(writer, player.Position);
                    WriteVector3(writer, player.Velocity);
                    writer.Write((byte)player.Kind);
                    writer.Write(player.SourceZoneId);
                }
                writer.Write(snapshot.ResourceNodes.Length);
                foreach (var node in snapshot.ResourceNodes)
                {
                    writer.Write(node.NodeId);
                    writer.Write(node.ResourceId);
                    WriteVector3(writer, node.Position);
                    writer.Write(node.Remaining);
                    writer.Write(node.MaxAmount);
                }
                writer.Write(snapshot.Mobs.Length);
                foreach (var mob in snapshot.Mobs)
                {
                    writer.Write(mob.MobId);
                    writer.Write(mob.MobTypeId);
                    WriteVector3(writer, mob.Position);
                    WriteVector3(writer, mob.Velocity);
                    writer.Write(mob.State);
                    writer.Write(mob.HitPoints);
                    writer.Write(mob.MaxHitPoints);
                }

                writer.Write(snapshot.Npcs.Length);
                foreach (var npc in snapshot.Npcs)
                {
                    writer.Write(npc.NpcId);
                    writer.Write(npc.NpcTypeId);
                    writer.Write(npc.DisplayName);
                    WriteVector3(writer, npc.Position);
                    writer.Write(npc.PrimaryRole);
                    writer.Write(npc.Services.Length);
                foreach (var service in npc.Services)
                {
                    writer.Write(service);
                }
                writer.Write(npc.GreetingText);
                writer.Write(npc.ServiceOptions.Length);
                foreach (var option in npc.ServiceOptions)
                {
                    writer.Write(option.ActionId);
                    writer.Write(option.Label);
                    writer.Write(option.UiHint);
                }
            }

                break;
            case TransferProbeMessage probe:
                WriteGuid(writer, probe.SessionId);
                writer.Write(probe.TransferToken);
                break;
            case TransferReadyMessage ready:
                WriteGuid(writer, ready.SessionId);
                writer.Write(ready.ZoneId);
                break;
            default:
                throw new InvalidDataException($"Unsupported UDP message type {message.GetType().Name}.");
        }
    }

    private static UdpMessage ReadUdpPayload(BinaryReader reader, UdpMessageKind kind)
    {
        return kind switch
        {
            UdpMessageKind.ClientInput => new ClientInputMessage(
                ReadGuid(reader),
                reader.ReadUInt32(),
                ReadVector3(reader),
                reader.ReadSingle()),
            UdpMessageKind.WorldSnapshot => ReadSnapshot(reader),
            UdpMessageKind.TransferProbe => new TransferProbeMessage(
                ReadGuid(reader),
                reader.ReadString()),
            UdpMessageKind.TransferReady => new TransferReadyMessage(
                ReadGuid(reader),
                reader.ReadInt32()),
            _ => throw new InvalidDataException($"Unsupported UDP message kind {kind}.")
        };
    }

    private static WorldSnapshotMessage ReadSnapshot(BinaryReader reader)
    {
        var zoneId = reader.ReadInt32();
        var tick = reader.ReadUInt32();
        var count = reader.ReadInt32();
        var players = new PlayerSnapshot[count];
        for (var i = 0; i < count; i++)
        {
            players[i] = new PlayerSnapshot(
                reader.ReadUInt64(),
                ReadVector3(reader),
                ReadVector3(reader),
                (SnapshotEntityKind)reader.ReadByte(),
                reader.ReadInt32());
        }

        var nodeCount = reader.ReadInt32();
        var nodes = new ResourceNodeSnapshot[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            nodes[i] = new ResourceNodeSnapshot(
                reader.ReadString(),
                reader.ReadString(),
                ReadVector3(reader),
                reader.ReadInt32(),
                reader.ReadInt32());
        }

        var mobCount = reader.ReadInt32();
        var mobs = new MobSnapshot[mobCount];
        for (var i = 0; i < mobCount; i++)
        {
            mobs[i] = new MobSnapshot(
                reader.ReadString(),
                reader.ReadString(),
                ReadVector3(reader),
                ReadVector3(reader),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32());
        }

        var npcCount = reader.ReadInt32();
        var npcs = new NpcSnapshot[npcCount];
        for (var i = 0; i < npcCount; i++)
        {
            var npcId = reader.ReadString();
            var npcTypeId = reader.ReadString();
            var displayName = reader.ReadString();
            var position = ReadVector3(reader);
            var primaryRole = reader.ReadString();
            var serviceCount = reader.ReadInt32();
            var services = new string[serviceCount];
            for (var serviceIndex = 0; serviceIndex < serviceCount; serviceIndex++)
            {
                services[serviceIndex] = reader.ReadString();
            }
            var greetingText = reader.ReadString();
            var optionCount = reader.ReadInt32();
            var serviceOptions = new NpcServiceSnapshot[optionCount];
            for (var optionIndex = 0; optionIndex < optionCount; optionIndex++)
            {
                serviceOptions[optionIndex] = new NpcServiceSnapshot(
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadString());
            }

            npcs[i] = new NpcSnapshot(
                npcId,
                npcTypeId,
                displayName,
                position,
                primaryRole,
                services,
                greetingText,
                serviceOptions);
        }

        return new WorldSnapshotMessage(zoneId, tick, players, nodes, mobs, npcs);
    }

    private static ZoneMobStateUpdateMessage ReadZoneMobStateUpdate(BinaryReader reader)
    {
        var zoneId = reader.ReadInt32();
        var count = reader.ReadInt32();
        var mobs = new MobSnapshot[count];
        for (var i = 0; i < count; i++)
        {
            mobs[i] = new MobSnapshot(
                reader.ReadString(),
                reader.ReadString(),
                ReadVector3(reader),
                ReadVector3(reader),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32());
        }

        return new ZoneMobStateUpdateMessage(zoneId, mobs);
    }

    private static ZoneStateBatchUpdateMessage ReadZoneStateBatchUpdate(BinaryReader reader)
    {
        var zoneId = reader.ReadInt32();
        var count = reader.ReadInt32();
        var players = new ZonePlayerStateUpdate[count];
        for (var i = 0; i < count; i++)
        {
            players[i] = new ZonePlayerStateUpdate(
                ReadGuid(reader),
                reader.ReadUInt64(),
                ReadVector3(reader),
                ReadVector3(reader));
        }

        return new ZoneStateBatchUpdateMessage(zoneId, players);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int byteCount, CancellationToken cancellationToken)
    {
        var buffer = new byte[byteCount];
        var offset = 0;
        while (offset < byteCount)
        {
            var read = await stream.ReadAsync(buffer, offset, byteCount - offset, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Remote endpoint closed the connection.");
            }

            offset += read;
        }

        return buffer;
    }

    private static void WriteGuid(BinaryWriter writer, Guid value)
        => writer.Write(value.ToByteArray());

    private static Guid ReadGuid(BinaryReader reader)
        => new(reader.ReadBytes(16));

    private static void WriteVector3(BinaryWriter writer, NetworkVector3 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }

    private static NetworkVector3 ReadVector3(BinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
