using System;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor.Authoring
{
public sealed class PlayerSpawnMarker : MonoBehaviour
{
    [SerializeField] private string spawnId = string.Empty;
    [SerializeField] private bool isDefaultSpawn = true;
    [SerializeField] private float facingYaw;
    [SerializeField] private string spawnTag = "starter";

    public string SpawnId => spawnId;
    public bool IsDefaultSpawn => isDefaultSpawn;
    public float FacingYaw => facingYaw;
    public string SpawnTag => spawnTag;

    private void Reset()
    {
        EnsureId();
    }

    private void OnValidate()
    {
        EnsureId();
        if (string.IsNullOrWhiteSpace(spawnTag))
        {
            spawnTag = "starter";
        }
    }

    private void EnsureId()
    {
        if (string.IsNullOrWhiteSpace(spawnId))
        {
            spawnId = "spawn-" + Guid.NewGuid().ToString("N")[..8];
        }
    }
}
}
