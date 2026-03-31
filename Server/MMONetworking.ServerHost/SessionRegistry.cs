using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<Guid, SessionRecord> _sessions = new();

    public SessionRecord CreateSession(string accountId, string accountName, ulong characterId, int initialZoneId, NetworkVector3 spawnPosition)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var session = new SessionRecord(
            Guid.NewGuid(),
            characterId,
            accountId,
            accountName,
            initialZoneId,
            spawnPosition,
            nowUtc);

        _sessions[session.SessionId] = session;
        return session;
    }

    public bool TryGet(Guid sessionId, out SessionRecord? session)
        => _sessions.TryGetValue(sessionId, out session);

    public bool TryRemove(Guid sessionId, out SessionRecord? session)
        => _sessions.TryRemove(sessionId, out session);

    public string IssueTransferToken(Guid sessionId, int zoneId, NetworkVector3 spawnPosition)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found.");
        }

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        session.PendingAttachments[zoneId] = new PendingAttachment(zoneId, token, spawnPosition);
        return token;
    }

    public bool TryConsumeAttachment(Guid sessionId, int zoneId, string token, out SessionRecord? session, out PendingAttachment attachment)
    {
        attachment = default;
        if (!_sessions.TryGetValue(sessionId, out session))
        {
            return false;
        }

        if (!session.PendingAttachments.TryGetValue(zoneId, out attachment))
        {
            return false;
        }

        if (!StringComparer.Ordinal.Equals(attachment.TransferToken, token))
        {
            return false;
        }

        session.PendingAttachments.TryRemove(zoneId, out _);
        return true;
    }

    public void SetCurrentZone(Guid sessionId, int zoneId, NetworkVector3 position)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.CurrentZoneId = zoneId;
            session.LastKnownPosition = position;
        }
    }

    public void TouchTcp(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.LastTcpSeenUtc = DateTimeOffset.UtcNow;
            session.DisconnectGraceDeadlineUtc = null;
        }
    }

    public void TouchUdp(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.LastUdpSeenUtc = DateTimeOffset.UtcNow;
            session.DisconnectGraceDeadlineUtc = null;
        }
    }

    public bool IsTimedOut(Guid sessionId, TimeSpan timeout)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var lastSeenUtc = session.LastTcpSeenUtc > session.LastUdpSeenUtc
            ? session.LastTcpSeenUtc
            : session.LastUdpSeenUtc;

        return nowUtc - lastSeenUtc > timeout;
    }

    public bool BeginDisconnectGrace(Guid sessionId, TimeSpan gracePeriod, out DateTimeOffset deadlineUtc)
    {
        deadlineUtc = default;
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        if (session.DisconnectGraceDeadlineUtc.HasValue)
        {
            deadlineUtc = session.DisconnectGraceDeadlineUtc.Value;
            return false;
        }

        deadlineUtc = DateTimeOffset.UtcNow.Add(gracePeriod);
        session.DisconnectGraceDeadlineUtc = deadlineUtc;
        return true;
    }

    public bool IsGraceExpired(Guid sessionId, out DateTimeOffset? deadlineUtc)
    {
        deadlineUtc = null;
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return false;
        }

        deadlineUtc = session.DisconnectGraceDeadlineUtc;
        return deadlineUtc.HasValue && DateTimeOffset.UtcNow >= deadlineUtc.Value;
    }

    public SessionSnapshot[] CreateDashboardSnapshot()
        => _sessions.Values
            .OrderBy(session => session.PlayerId)
            .Select(session => new SessionSnapshot(
                session.SessionId,
                session.PlayerId,
                session.AccountId,
                session.AccountName,
                session.CurrentZoneId,
                session.LastKnownPosition.X,
                session.LastKnownPosition.Y,
                session.LastKnownPosition.Z,
                session.LastTcpSeenUtc,
                session.LastUdpSeenUtc,
                session.DisconnectGraceDeadlineUtc,
                session.PendingAttachments.Values
                    .OrderBy(attachment => attachment.ZoneId)
                    .Select(attachment => new PendingAttachmentSnapshot(
                        attachment.ZoneId,
                        attachment.TransferToken,
                        attachment.SpawnPosition.X,
                        attachment.SpawnPosition.Y,
                        attachment.SpawnPosition.Z))
                    .ToArray()))
            .ToArray();

    public readonly record struct PendingAttachment(int ZoneId, string TransferToken, NetworkVector3 SpawnPosition);

    public sealed class SessionRecord
    {
        public SessionRecord(Guid sessionId, ulong playerId, string accountId, string accountName, int currentZoneId, NetworkVector3 spawnPosition, DateTimeOffset createdAtUtc)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            AccountId = accountId;
            AccountName = accountName;
            CurrentZoneId = currentZoneId;
            LastKnownPosition = spawnPosition;
            CreatedAtUtc = createdAtUtc;
            LastTcpSeenUtc = createdAtUtc;
            LastUdpSeenUtc = createdAtUtc;
        }

        public Guid SessionId { get; }
        public ulong PlayerId { get; }
        public string AccountId { get; }
        public string AccountName { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset LastTcpSeenUtc { get; set; }
        public DateTimeOffset LastUdpSeenUtc { get; set; }
        public DateTimeOffset? DisconnectGraceDeadlineUtc { get; set; }
        public int CurrentZoneId { get; set; }
        public NetworkVector3 LastKnownPosition { get; set; }
        public ConcurrentDictionary<int, PendingAttachment> PendingAttachments { get; } = new();
    }
}
