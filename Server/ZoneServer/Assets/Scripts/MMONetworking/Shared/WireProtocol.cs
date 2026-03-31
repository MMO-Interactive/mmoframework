using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MMONetworking
{
public static class WireProtocol
{
    public static async Task WriteTcpMessageAsync(Stream stream, TcpMessage message, CancellationToken cancellationToken = default)
    {
        using (var payloadStream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, true))
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
    }

    public static async Task<TcpMessage> ReadTcpMessageAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var lengthBytes = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var payloadLength = BitConverter.ToInt32(lengthBytes, 0);
        var payloadBytes = await ReadExactAsync(stream, payloadLength, cancellationToken).ConfigureAwait(false);
        using (var payloadStream = new MemoryStream(payloadBytes, false))
        using (var reader = new BinaryReader(payloadStream, Encoding.UTF8, true))
        {
            var kind = (TcpMessageKind)reader.ReadUInt16();
            return ReadTcpPayload(reader, kind);
        }
    }

    public static byte[] SerializeUdpMessage(UdpMessage message)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((ushort)message.Kind);
            WriteUdpPayload(writer, message);
            return stream.ToArray();
        }
    }

    public static UdpMessage DeserializeUdpMessage(byte[] payload)
    {
        using (var stream = new MemoryStream(payload, false))
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            var kind = (UdpMessageKind)reader.ReadUInt16();
            return ReadUdpPayload(reader, kind);
        }
    }

    private static void WriteTcpPayload(BinaryWriter writer, TcpMessage message)
    {
        if (message is AttachToZoneMessage attach)
        {
            WriteGuid(writer, attach.SessionId);
            writer.Write(attach.PlayerId);
            writer.Write(attach.TransferToken);
            return;
        }

        if (message is AttachAcceptedMessage accepted)
        {
            WriteGuid(writer, accepted.SessionId);
            writer.Write(accepted.ZoneId);
            WriteVector3(writer, accepted.SpawnPosition);
            return;
        }

        if (message is ZoneTransferPrepareMessage transferPrepare)
        {
            WriteGuid(writer, transferPrepare.SessionId);
            WriteGuid(writer, transferPrepare.TransferId);
            writer.Write(transferPrepare.FromZoneId);
            writer.Write(transferPrepare.ToZoneId);
            writer.Write(transferPrepare.ZoneHost);
            writer.Write(transferPrepare.ZoneTcpPort);
            writer.Write(transferPrepare.ZoneUdpPort);
            writer.Write(transferPrepare.TransferToken);
            WriteVector3(writer, transferPrepare.SpawnPosition);
            return;
        }

        if (message is ZoneTransferCommittedMessage committed)
        {
            WriteGuid(writer, committed.SessionId);
            WriteGuid(writer, committed.TransferId);
            writer.Write(committed.ZoneId);
            return;
        }

        if (message is HeartbeatMessage heartbeat)
        {
            writer.Write(heartbeat.ServerTicks);
            return;
        }

        if (message is ErrorMessage error)
        {
            writer.Write(error.Text);
            return;
        }

        if (message is ZoneAttachAuthorizeMessage authorize)
        {
            WriteGuid(writer, authorize.SessionId);
            writer.Write(authorize.PlayerId);
            writer.Write(authorize.ZoneId);
            writer.Write(authorize.TransferToken);
            return;
        }

        if (message is ZoneAttachAuthorizedMessage authorized)
        {
            writer.Write(authorized.Success);
            WriteGuid(writer, authorized.SessionId);
            writer.Write(authorized.PlayerId);
            writer.Write(authorized.ZoneId);
            WriteVector3(writer, authorized.SpawnPosition);
            writer.Write(authorized.ErrorText);
            return;
        }

        if (message is ZoneStateUpdateMessage stateUpdate)
        {
            WriteGuid(writer, stateUpdate.SessionId);
            writer.Write(stateUpdate.ZoneId);
            WriteVector3(writer, stateUpdate.Position);
            return;
        }

        if (message is ZoneTransferRequestMessage transferRequest)
        {
            WriteGuid(writer, transferRequest.SessionId);
            writer.Write(transferRequest.ZoneId);
            WriteVector3(writer, transferRequest.Position);
            return;
        }

        if (message is ZoneTransferResponseMessage transferResponse)
        {
            writer.Write(transferResponse.ShouldTransfer);
            WriteGuid(writer, transferResponse.SessionId);
            writer.Write(transferResponse.FromZoneId);
            writer.Write(transferResponse.ToZoneId);
            writer.Write(transferResponse.ZoneHost);
            writer.Write(transferResponse.ZoneTcpPort);
            writer.Write(transferResponse.ZoneUdpPort);
            writer.Write(transferResponse.TransferToken);
            WriteVector3(writer, transferResponse.SpawnPosition);
            writer.Write(transferResponse.ErrorText);
            return;
        }

        if (message is ZonePrewarmRequestMessage prewarmRequest)
        {
            WriteGuid(writer, prewarmRequest.SessionId);
            writer.Write(prewarmRequest.ZoneId);
            WriteVector3(writer, prewarmRequest.Position);
            return;
        }

        if (message is ZonePrewarmResponseMessage prewarmResponse)
        {
            writer.Write(prewarmResponse.Started);
            WriteGuid(writer, prewarmResponse.SessionId);
            writer.Write(prewarmResponse.ZoneId);
            writer.Write(prewarmResponse.DestinationZoneId);
            writer.Write(prewarmResponse.ErrorText);
            return;
        }

        if (message is GameplayCommandMessage gameplayCommand)
        {
            WriteGuid(writer, gameplayCommand.SessionId);
            writer.Write(gameplayCommand.CommandId);
            writer.Write((byte)gameplayCommand.CommandKind);
            writer.Write(gameplayCommand.TargetId);
            return;
        }

        if (message is GameplayResultMessage gameplayResult)
        {
            WriteGuid(writer, gameplayResult.SessionId);
            writer.Write(gameplayResult.CommandId);
            writer.Write(gameplayResult.Success);
            writer.Write(gameplayResult.Text);
            writer.Write(gameplayResult.ItemId);
            writer.Write(gameplayResult.ItemCount);
            writer.Write(gameplayResult.SkillValue);
            return;
        }

        throw new InvalidDataException("Unsupported TCP message.");
    }

    private static TcpMessage ReadTcpPayload(BinaryReader reader, TcpMessageKind kind)
    {
        switch (kind)
        {
            case TcpMessageKind.AttachToZone:
                return new AttachToZoneMessage(ReadGuid(reader), reader.ReadUInt64(), reader.ReadString());
            case TcpMessageKind.AttachAccepted:
                return new AttachAcceptedMessage(ReadGuid(reader), reader.ReadInt32(), ReadVector3(reader));
            case TcpMessageKind.ZoneTransferPrepare:
                return new ZoneTransferPrepareMessage(ReadGuid(reader), ReadGuid(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), ReadVector3(reader));
            case TcpMessageKind.ZoneTransferCommitted:
                return new ZoneTransferCommittedMessage(ReadGuid(reader), ReadGuid(reader), reader.ReadInt32());
            case TcpMessageKind.Heartbeat:
                return new HeartbeatMessage(reader.ReadInt64());
            case TcpMessageKind.Error:
                return new ErrorMessage(reader.ReadString());
            case TcpMessageKind.ZoneAttachAuthorize:
                return new ZoneAttachAuthorizeMessage(ReadGuid(reader), reader.ReadUInt64(), reader.ReadInt32(), reader.ReadString());
            case TcpMessageKind.ZoneAttachAuthorized:
                return new ZoneAttachAuthorizedMessage(reader.ReadBoolean(), ReadGuid(reader), reader.ReadUInt64(), reader.ReadInt32(), ReadVector3(reader), reader.ReadString());
            case TcpMessageKind.ZoneStateUpdate:
                return new ZoneStateUpdateMessage(ReadGuid(reader), reader.ReadInt32(), ReadVector3(reader));
            case TcpMessageKind.ZoneTransferRequest:
                return new ZoneTransferRequestMessage(ReadGuid(reader), reader.ReadInt32(), ReadVector3(reader));
            case TcpMessageKind.ZoneTransferResponse:
                return new ZoneTransferResponseMessage(reader.ReadBoolean(), ReadGuid(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(), ReadVector3(reader), reader.ReadString());
            case TcpMessageKind.ZonePrewarmRequest:
                return new ZonePrewarmRequestMessage(ReadGuid(reader), reader.ReadInt32(), ReadVector3(reader));
            case TcpMessageKind.ZonePrewarmResponse:
                return new ZonePrewarmResponseMessage(reader.ReadBoolean(), ReadGuid(reader), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString());
            case TcpMessageKind.GameplayCommand:
                return new GameplayCommandMessage(ReadGuid(reader), reader.ReadUInt32(), (GameplayCommandKind)reader.ReadByte(), reader.ReadString());
            case TcpMessageKind.GameplayResult:
                return new GameplayResultMessage(ReadGuid(reader), reader.ReadUInt32(), reader.ReadBoolean(), reader.ReadString(), reader.ReadString(), reader.ReadInt32(), reader.ReadInt32());
            default:
                throw new InvalidDataException("Unsupported TCP message kind.");
        }
    }

    private static void WriteUdpPayload(BinaryWriter writer, UdpMessage message)
    {
        if (message is ClientInputMessage input)
        {
            WriteGuid(writer, input.SessionId);
            writer.Write(input.Sequence);
            WriteVector3(writer, input.Move);
            writer.Write(input.DeltaTimeSeconds);
            return;
        }

        if (message is WorldSnapshotMessage snapshot)
        {
            writer.Write(snapshot.ZoneId);
            writer.Write(snapshot.Tick);
            writer.Write(snapshot.Players.Length);
            for (var i = 0; i < snapshot.Players.Length; i++)
            {
                var player = snapshot.Players[i];
                writer.Write(player.PlayerId);
                WriteVector3(writer, player.Position);
                WriteVector3(writer, player.Velocity);
                writer.Write((byte)player.Kind);
                writer.Write(player.SourceZoneId);
            }

            return;
        }

        if (message is TransferProbeMessage probe)
        {
            WriteGuid(writer, probe.SessionId);
            writer.Write(probe.TransferToken);
            return;
        }

        if (message is TransferReadyMessage ready)
        {
            WriteGuid(writer, ready.SessionId);
            writer.Write(ready.ZoneId);
            return;
        }

        throw new InvalidDataException("Unsupported UDP message.");
    }

    private static UdpMessage ReadUdpPayload(BinaryReader reader, UdpMessageKind kind)
    {
        switch (kind)
        {
            case UdpMessageKind.ClientInput:
                return new ClientInputMessage(ReadGuid(reader), reader.ReadUInt32(), ReadVector3(reader), reader.ReadSingle());
            case UdpMessageKind.WorldSnapshot:
                return ReadSnapshot(reader);
            case UdpMessageKind.TransferProbe:
                return new TransferProbeMessage(ReadGuid(reader), reader.ReadString());
            case UdpMessageKind.TransferReady:
                return new TransferReadyMessage(ReadGuid(reader), reader.ReadInt32());
            default:
                throw new InvalidDataException("Unsupported UDP message kind.");
        }
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

        return new WorldSnapshotMessage(zoneId, tick, players);
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
        => new Guid(reader.ReadBytes(16));

    private static void WriteVector3(BinaryWriter writer, NetworkVector3 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }

    private static NetworkVector3 ReadVector3(BinaryReader reader)
        => new NetworkVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
}
