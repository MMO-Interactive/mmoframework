using System;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor.Authoring
{
public sealed class MobSpawnMarker : MonoBehaviour
{
    [SerializeField] private string spawnId = string.Empty;
    [SerializeField] private string mobTypeId = "wolf";
    [SerializeField] private int count = 3;
    [SerializeField] private float radius = 8f;
    [SerializeField] private float roamRadius = 14f;

    public string SpawnId => spawnId;
    public string MobTypeId => mobTypeId;
    public int Count => count;
    public float Radius => radius;
    public float RoamRadius => roamRadius;

    private void Reset()
    {
        EnsureId();
    }

    private void OnValidate()
    {
        EnsureId();
        if (string.IsNullOrWhiteSpace(mobTypeId))
        {
            mobTypeId = "wolf";
        }

        count = Math.Max(1, count);
        radius = Mathf.Max(0.5f, radius);
        roamRadius = Mathf.Max(radius, roamRadius);
    }

    private void EnsureId()
    {
        if (string.IsNullOrWhiteSpace(spawnId))
        {
            spawnId = "mob-" + Guid.NewGuid().ToString("N")[..8];
        }
    }
}
}
