using System.Collections.Generic;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoRemoteAvatarSystem : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private Color remotePlayerColor = new Color(0.87f, 0.44f, 0.2f);
    [SerializeField] private Color ghostPlayerColor = new Color(0.2f, 0.67f, 0.87f);

    private readonly Dictionary<ulong, RemoteAvatar> _avatars = new Dictionary<ulong, RemoteAvatar>();
    private readonly HashSet<ulong> _seenThisFrame = new HashSet<ulong>();

    private void Awake()
    {
        if (client == null)
        {
            client = FindObjectOfType<UnityMmoClient>();
        }

        if (client != null)
        {
            client.SnapshotReceived += HandleSnapshot;
        }
    }

    private void OnDestroy()
    {
        if (client != null)
        {
            client.SnapshotReceived -= HandleSnapshot;
        }
    }

    private void HandleSnapshot(WorldSnapshotMessage snapshot)
    {
        _seenThisFrame.Clear();

        for (var i = 0; i < snapshot.Players.Length; i++)
        {
            var player = snapshot.Players[i];
            if (player.PlayerId == client.PlayerId)
            {
                continue;
            }

            _seenThisFrame.Add(player.PlayerId);
            if (!_avatars.TryGetValue(player.PlayerId, out var avatar))
            {
                avatar = CreateAvatar(player.PlayerId);
                _avatars[player.PlayerId] = avatar;
            }

            avatar.TargetPosition = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);
            var styleChanged = avatar.LastKind != player.Kind || avatar.SourceZoneId != player.SourceZoneId;
            avatar.LastKind = player.Kind;
            avatar.SourceZoneId = player.SourceZoneId;
            avatar.LastSeenTime = Time.time;
            if (styleChanged)
            {
                ApplyAvatarStyle(avatar);
            }
        }

        foreach (var pair in new List<KeyValuePair<ulong, RemoteAvatar>>(_avatars))
        {
            if (_seenThisFrame.Contains(pair.Key))
            {
                continue;
            }

            if (Time.time - pair.Value.LastSeenTime > 1.2f)
            {
                Destroy(pair.Value.GameObject);
                _avatars.Remove(pair.Key);
            }
        }
    }

    private void Update()
    {
        foreach (var avatar in _avatars.Values)
        {
            avatar.GameObject.transform.position = Vector3.Lerp(
                avatar.GameObject.transform.position,
                avatar.TargetPosition,
                Time.deltaTime * 10f);
        }
    }

    private RemoteAvatar CreateAvatar(ulong playerId)
    {
        var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gameObject.name = "Remote Avatar " + playerId;
        gameObject.transform.localScale = new Vector3(0.9f, 1.8f, 0.9f);
        var renderer = gameObject.GetComponent<Renderer>();

        var avatar = new RemoteAvatar
        {
            PlayerId = playerId,
            GameObject = gameObject,
            Renderer = renderer,
            TargetPosition = gameObject.transform.position,
            LastSeenTime = Time.time
        };
        ApplyAvatarStyle(avatar);
        return avatar;
    }

    private void ApplyAvatarStyle(RemoteAvatar avatar)
    {
        var isGhost = avatar.LastKind == SnapshotEntityKind.Ghost;
        avatar.GameObject.name = (isGhost ? "Ghost Avatar " : "Remote Avatar ") + avatar.PlayerId + " (src " + avatar.SourceZoneId + ")";
        avatar.GameObject.transform.localScale = isGhost
            ? new Vector3(0.75f, 1.45f, 0.75f)
            : new Vector3(0.9f, 1.8f, 0.9f);

        if (avatar.Renderer != null)
        {
            UnityMmoMaterialFactory.Apply(avatar.Renderer, isGhost ? ghostPlayerColor : remotePlayerColor);
        }
    }

    private sealed class RemoteAvatar
    {
        public ulong PlayerId;
        public GameObject GameObject;
        public Renderer Renderer;
        public Vector3 TargetPosition;
        public float LastSeenTime;
        public SnapshotEntityKind LastKind;
        public int SourceZoneId;
    }
}
}
