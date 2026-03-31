using System.Collections.Generic;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoMobSystem : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private Color wolfColor = new Color(0.82f, 0.38f, 0.38f);
    [SerializeField] private Color boarColor = new Color(0.62f, 0.32f, 0.22f);
    [SerializeField] private Color fallbackColor = new Color(0.78f, 0.44f, 0.34f);

    private readonly Dictionary<string, MobVisual> _mobs = new Dictionary<string, MobVisual>();
    private readonly HashSet<string> _seenThisSnapshot = new HashSet<string>();
    private Transform _container;

    private void Awake()
    {
        var containerObject = new GameObject("Mob Visuals");
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
        for (var i = 0; i < snapshot.Mobs.Length; i++)
        {
            var mob = snapshot.Mobs[i];
            _seenThisSnapshot.Add(mob.MobId);

            if (!_mobs.TryGetValue(mob.MobId, out var visual))
            {
                visual = CreateMobVisual(mob);
                _mobs[mob.MobId] = visual;
            }

            visual.TargetPosition = new Vector3(mob.Position.X, mob.Position.Y + 0.9f, mob.Position.Z);
            visual.MobTypeId = mob.MobTypeId;
            visual.State = mob.State;
            ApplyStyle(visual);
        }

        foreach (var pair in new List<KeyValuePair<string, MobVisual>>(_mobs))
        {
            if (_seenThisSnapshot.Contains(pair.Key))
            {
                continue;
            }

            Destroy(pair.Value.GameObject);
            _mobs.Remove(pair.Key);
        }
    }

    private void Update()
    {
        foreach (var mob in _mobs.Values)
        {
            mob.GameObject.transform.position = Vector3.Lerp(
                mob.GameObject.transform.position,
                mob.TargetPosition,
                Time.deltaTime * 8f);
        }
    }

    private MobVisual CreateMobVisual(MobSnapshot mob)
    {
        var primitive = IsBoar(mob.MobTypeId) ? PrimitiveType.Cube : PrimitiveType.Capsule;
        var gameObject = GameObject.CreatePrimitive(primitive);
        gameObject.transform.SetParent(_container, false);
        gameObject.name = "Mob " + mob.MobId;

        return new MobVisual
        {
            GameObject = gameObject,
            Renderer = gameObject.GetComponent<Renderer>(),
            TargetPosition = new Vector3(mob.Position.X, mob.Position.Y + 0.9f, mob.Position.Z),
            MobTypeId = mob.MobTypeId,
            State = mob.State
        };
    }

    private void ApplyStyle(MobVisual visual)
    {
        var isBoar = IsBoar(visual.MobTypeId);
        visual.GameObject.name = "Mob " + visual.MobTypeId + " (" + visual.State + ")";
        visual.GameObject.transform.localScale = isBoar
            ? new Vector3(1.2f, 0.9f, 0.9f)
            : new Vector3(0.9f, 1.5f, 0.9f);

        if (visual.Renderer != null)
        {
            var color = fallbackColor;
            if (IsWolf(visual.MobTypeId))
            {
                color = wolfColor;
            }
            else if (isBoar)
            {
                color = boarColor;
            }

            UnityMmoMaterialFactory.Apply(visual.Renderer, color);
        }
    }

    private static bool IsWolf(string mobTypeId)
        => !string.IsNullOrWhiteSpace(mobTypeId) &&
           mobTypeId.IndexOf("wolf", System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsBoar(string mobTypeId)
        => !string.IsNullOrWhiteSpace(mobTypeId) &&
           mobTypeId.IndexOf("boar", System.StringComparison.OrdinalIgnoreCase) >= 0;

    private sealed class MobVisual
    {
        public GameObject GameObject;
        public Renderer Renderer;
        public Vector3 TargetPosition;
        public string MobTypeId;
        public string State;
    }
}
}
