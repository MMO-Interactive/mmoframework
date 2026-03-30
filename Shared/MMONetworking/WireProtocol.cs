using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MMONetworking;

public static class WireProtocol
{
    public const int CurrentProtocolVersion = 1;

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
                reader.ReadInt32()),
            TcpMessageKind.HelloAccepted => new HelloAcceptedMessage(
                ReadGuid(reader),
                reader.ReadUInt64(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
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
                ReadVector3(reader)),
            TcpMessageKind.ZoneTransferCommitted => new ZoneTransferCommittedMessage(
                ReadGuid(reader),
                ReadGuid(reader),
                reader.ReadInt32()),
            TcpMessageKind.Heartbeat => new HeartbeatMessage(reader.ReadInt64()),
            TcpMessageKind.Error => new ErrorMessage(reader.ReadString()),
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
