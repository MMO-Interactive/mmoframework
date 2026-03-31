using System.Collections.Generic;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoResourceNodeSystem : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private Color treeNodeColor = new Color(0.24f, 0.58f, 0.24f);
    [SerializeField] private Color oreNodeColor = new Color(0.5f, 0.54f, 0.6f);
    [SerializeField] private Color fallbackNodeColor = new Color(0.74f, 0.68f, 0.42f);

    private readonly Dictionary<string, NodeVisual> _nodes = new Dictionary<string, NodeVisual>();
    private readonly HashSet<string> _seenThisSnapshot = new HashSet<string>();
    private Transform _container;

    private void Awake()
    {
        var containerObject = new GameObject("Resource Node Visuals");
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
        if (snapshot.ResourceNodes.Length > 0 && _nodes.Count == 0)
        {
            Debug.Log("Rendering " + snapshot.ResourceNodes.Length + " resource nodes for zone " + snapshot.ZoneId + ".");
        }

        _seenThisSnapshot.Clear();
        for (var i = 0; i < snapshot.ResourceNodes.Length; i++)
        {
            var node = snapshot.ResourceNodes[i];
            _seenThisSnapshot.Add(node.NodeId);

            if (!_nodes.TryGetValue(node.NodeId, out var visual))
            {
                visual = CreateNodeVisual(node);
                _nodes[node.NodeId] = visual;
            }

            visual.GameObject.transform.position = new Vector3(node.Position.X, node.Position.Y + 0.65f, node.Position.Z);
            visual.ResourceId = node.ResourceId;
            visual.LastSeenTime = Time.time;
            ApplyStyle(visual);
        }

        foreach (var pair in new List<KeyValuePair<string, NodeVisual>>(_nodes))
        {
            if (_seenThisSnapshot.Contains(pair.Key))
            {
                continue;
            }

            Destroy(pair.Value.GameObject);
            _nodes.Remove(pair.Key);
        }
    }

    private NodeVisual CreateNodeVisual(ResourceNodeSnapshot node)
    {
        var gameObject = GameObject.CreatePrimitive(IsOre(node.ResourceId) ? PrimitiveType.Cylinder : PrimitiveType.Capsule);
        gameObject.name = "Resource Node " + node.NodeId;
        gameObject.transform.SetParent(_container, false);
        var visual = new NodeVisual
        {
            GameObject = gameObject,
            Renderer = gameObject.GetComponent<Renderer>(),
            ResourceId = node.ResourceId,
            LastSeenTime = Time.time
        };

        ApplyStyle(visual);
        return visual;
    }

    private void ApplyStyle(NodeVisual visual)
    {
        var isOre = IsOre(visual.ResourceId);
        visual.GameObject.transform.localScale = isOre
            ? new Vector3(1.1f, 0.55f, 1.1f)
            : new Vector3(0.9f, 1.3f, 0.9f);

        if (visual.Renderer != null)
        {
            var color = fallbackNodeColor;
            if (IsTree(visual.ResourceId))
            {
                color = treeNodeColor;
            }
            else if (isOre)
            {
                color = oreNodeColor;
            }

            UnityMmoMaterialFactory.Apply(visual.Renderer, color);
        }
    }

    private static bool IsTree(string resourceId)
        => !string.IsNullOrWhiteSpace(resourceId) &&
           (resourceId.IndexOf("tree", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            resourceId.IndexOf("log", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            resourceId.IndexOf("wood", System.StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool IsOre(string resourceId)
        => !string.IsNullOrWhiteSpace(resourceId) &&
           (resourceId.IndexOf("ore", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            resourceId.IndexOf("stone", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            resourceId.IndexOf("rock", System.StringComparison.OrdinalIgnoreCase) >= 0);

    private sealed class NodeVisual
    {
        public GameObject GameObject;
        public Renderer Renderer;
        public string ResourceId;
        public float LastSeenTime;
    }
}
}
