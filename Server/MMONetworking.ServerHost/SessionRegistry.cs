using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<Guid, SessionRecord> _sessions = new();
    private long _nextPlayerId = 1000;

    public SessionRecord CreateSession(string accountId, int initialZoneId, NetworkVector3 spawnPosition)
    {
        var session = new SessionRecord(
            Guid.NewGuid(),
            (ulong)Interlocked.Increment(ref _nextPlayerId),
            accountId,
            initialZoneId,
            spawnPosition);

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

    public SessionSnapshot[] CreateDashboardSnapshot()
        => _sessions.Values
            .OrderBy(session => session.PlayerId)
            .Select(session => new SessionSnapshot(
                session.SessionId,
                session.PlayerId,
                session.AccountId,
                session.CurrentZoneId,
                session.LastKnownPosition.X,
                session.LastKnownPosition.Y,
                session.LastKnownPosition.Z,
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
        public SessionRecord(Guid sessionId, ulong playerId, string accountId, int currentZoneId, NetworkVector3 spawnPosition)
        {
            SessionId = sessionId;
            PlayerId = playerId;
            AccountId = accountId;
            CurrentZoneId = currentZoneId;
            LastKnownPosition = spawnPosition;
        }

        public Guid SessionId { get; }
        public ulong PlayerId { get; }
        public string AccountId { get; }
        public int CurrentZoneId { get; set; }
        public NetworkVector3 LastKnownPosition { get; set; }
        public ConcurrentDictionary<int, PendingAttachment> PendingAttachments { get; } = new();
    }
}
