using System;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor.Authoring
{
public sealed class ResourceNodeMarker : MonoBehaviour
{
    [SerializeField] private string nodeId = string.Empty;
    [SerializeField] private string resourceId = "wood";
    [SerializeField] private int maxCharges = 5;
    [SerializeField] private float respawnSeconds = 30f;

    public string NodeId => nodeId;
    public string ResourceId => resourceId;
    public int MaxCharges => maxCharges;
    public float RespawnSeconds => respawnSeconds;

    private void Reset()
    {
        EnsureId();
    }

    private void OnValidate()
    {
        EnsureId();
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            resourceId = "wood";
        }

        maxCharges = Math.Max(1, maxCharges);
        respawnSeconds = Mathf.Max(1f, respawnSeconds);
    }

    private void EnsureId()
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = "node-" + Guid.NewGuid().ToString("N")[..8];
        }
    }
}
}
