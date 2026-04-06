using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoNpcSystem : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private Color shopColor = new Color(0.92f, 0.74f, 0.32f);
    [SerializeField] private Color questColor = new Color(0.40f, 0.86f, 0.62f);
    [SerializeField] private Color trainerColor = new Color(0.43f, 0.67f, 0.96f);
    [SerializeField] private Color fallbackColor = new Color(0.78f, 0.72f, 0.60f);

    private readonly Dictionary<string, NpcVisual> _npcs = new Dictionary<string, NpcVisual>();
    private readonly Dictionary<string, NpcSnapshot> _snapshots = new Dictionary<string, NpcSnapshot>();
    private readonly HashSet<string> _seenThisSnapshot = new HashSet<string>();
    private Transform _container;

    private void Awake()
    {
        var containerObject = new GameObject("Npc Visuals");
        containerObject.transform.SetParent(transform, false);
        _container = containerObject.transform;

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
        _seenThisSnapshot.Clear();
        for (var i = 0; i < snapshot.Npcs.Length; i++)
        {
            var npc = snapshot.Npcs[i];
            _seenThisSnapshot.Add(npc.NpcId);

            NpcVisual visual;
            if (!_npcs.TryGetValue(npc.NpcId, out visual))
            {
                visual = CreateNpcVisual(npc);
                _npcs[npc.NpcId] = visual;
            }

            _snapshots[npc.NpcId] = npc;
            visual.TargetPosition = new Vector3(npc.Position.X, npc.Position.Y + 0.9f, npc.Position.Z);
            visual.NpcTypeId = npc.NpcTypeId;
            visual.DisplayName = npc.DisplayName;
            visual.PrimaryRole = npc.PrimaryRole;
            visual.Services = npc.Services ?? Array.Empty<string>();
            visual.ServiceOptions = npc.ServiceOptions ?? Array.Empty<NpcServiceSnapshot>();
            ApplyStyle(visual);
        }

        foreach (var pair in new List<KeyValuePair<string, NpcVisual>>(_npcs))
        {
            if (_seenThisSnapshot.Contains(pair.Key))
            {
                continue;
            }

            Destroy(pair.Value.GameObject);
            _npcs.Remove(pair.Key);
            _snapshots.Remove(pair.Key);
        }
    }

    public bool TryGetNearestNpc(Vector3 position, float maxDistance, out NpcSnapshot nearestNpc)
    {
        nearestNpc = default;
        var found = false;
        var bestDistanceSq = maxDistance * maxDistance;

        foreach (var snapshot in _snapshots.Values)
        {
            var npcPosition = new Vector3(snapshot.Position.X, snapshot.Position.Y, snapshot.Position.Z);
            var distanceSq = (npcPosition - position).sqrMagnitude;
            if (distanceSq > bestDistanceSq)
            {
                continue;
            }

            bestDistanceSq = distanceSq;
            nearestNpc = snapshot;
            found = true;
        }

        return found;
    }

    public IEnumerable<NpcSnapshot> GetVisibleNpcs()
        => _snapshots.Values;

    public bool TrySelectNpcFromRay(Ray ray, out NpcSnapshot npc)
    {
        npc = default;
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, 200f))
        {
            return false;
        }

        foreach (var pair in _npcs)
        {
            if (pair.Value.GameObject == hit.collider.gameObject)
            {
                return _snapshots.TryGetValue(pair.Key, out npc);
            }
        }

        return false;
    }

    private void Update()
    {
        foreach (var npc in _npcs.Values)
        {
            npc.GameObject.transform.position = Vector3.Lerp(
                npc.GameObject.transform.position,
                npc.TargetPosition,
                Time.deltaTime * 8f);
        }
    }

    private NpcVisual CreateNpcVisual(NpcSnapshot npc)
    {
        var primitive = IsQuestRole(npc.PrimaryRole, npc.ServiceOptions)
            ? PrimitiveType.Capsule
            : IsTrainerRole(npc.PrimaryRole, npc.Services, npc.ServiceOptions)
                ? PrimitiveType.Cube
                : PrimitiveType.Cylinder;
        var gameObject = GameObject.CreatePrimitive(primitive);
        gameObject.transform.SetParent(_container, false);
        gameObject.name = "NPC " + npc.DisplayName;

        return new NpcVisual
        {
            GameObject = gameObject,
            Renderer = gameObject.GetComponent<Renderer>(),
            TargetPosition = new Vector3(npc.Position.X, npc.Position.Y + 0.9f, npc.Position.Z),
            DisplayName = npc.DisplayName,
            NpcTypeId = npc.NpcTypeId,
            PrimaryRole = npc.PrimaryRole,
            Services = npc.Services ?? Array.Empty<string>(),
            ServiceOptions = npc.ServiceOptions ?? Array.Empty<NpcServiceSnapshot>()
        };
    }

    private void ApplyStyle(NpcVisual visual)
    {
        visual.GameObject.name = "NPC " + visual.DisplayName + " [" + visual.PrimaryRole + "]";
        visual.GameObject.transform.localScale = IsQuestRole(visual.PrimaryRole, visual.ServiceOptions)
            ? new Vector3(0.9f, 1.6f, 0.9f)
            : new Vector3(1.0f, 1.3f, 1.0f);

        if (visual.Renderer != null)
        {
            var color = fallbackColor;
            if (IsShopRole(visual.PrimaryRole, visual.Services, visual.ServiceOptions))
            {
                color = shopColor;
            }
            else if (IsQuestRole(visual.PrimaryRole, visual.ServiceOptions))
            {
                color = questColor;
            }
            else if (IsTrainerRole(visual.PrimaryRole, visual.Services, visual.ServiceOptions))
            {
                color = trainerColor;
            }

            UnityMmoMaterialFactory.Apply(visual.Renderer, color);
        }
    }

    private static bool IsShopRole(string primaryRole, string[] services, NpcServiceSnapshot[] serviceOptions)
    {
        if (HasAction(serviceOptions, "shop"))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(primaryRole) &&
            primaryRole.IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (services == null)
        {
            return false;
        }

        for (var i = 0; i < services.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(services[i]) &&
                (services[i].IndexOf("shop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 services[i].IndexOf("craft", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQuestRole(string primaryRole, NpcServiceSnapshot[] serviceOptions)
        => HasAction(serviceOptions, "quests") ||
           (!string.IsNullOrWhiteSpace(primaryRole) &&
            primaryRole.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool IsTrainerRole(string primaryRole, string[] services, NpcServiceSnapshot[] serviceOptions)
    {
        if (HasAction(serviceOptions, "training"))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(primaryRole) &&
            primaryRole.IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (services == null)
        {
            return false;
        }

        for (var i = 0; i < services.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(services[i]) &&
                (services[i].IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 services[i].IndexOf("progress", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAction(NpcServiceSnapshot[] serviceOptions, string actionId)
    {
        if (serviceOptions == null)
        {
            return false;
        }

        for (var i = 0; i < serviceOptions.Length; i++)
        {
            if (string.Equals(serviceOptions[i].ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class NpcVisual
    {
        public GameObject GameObject;
        public Renderer Renderer;
        public Vector3 TargetPosition;
        public string DisplayName;
        public string NpcTypeId;
        public string PrimaryRole;
        public string[] Services;
        public NpcServiceSnapshot[] ServiceOptions;
    }
}
}
