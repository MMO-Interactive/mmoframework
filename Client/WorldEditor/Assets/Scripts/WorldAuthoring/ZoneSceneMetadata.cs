using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RiseOfHeroes.WorldEditor.Authoring
{
public sealed class ZoneSceneMetadata : MonoBehaviour
{
    public const float StandardTerrainSizeMeters = 2000f;

    [SerializeField] private int zoneId = 1;
    [SerializeField] private string zoneName = "New Zone";
    [SerializeField] private string assetBundleName = string.Empty;
    [SerializeField] private Vector2 minBounds = new Vector2(0f, 0f);
    [SerializeField] private Vector2 maxBounds = new Vector2(StandardTerrainSizeMeters, StandardTerrainSizeMeters);
    [SerializeField] private string environmentTag = "temperate";
    [SerializeField] private string notes = string.Empty;

    public int ZoneId
    {
        get => zoneId;
        set => zoneId = Math.Max(1, value);
    }

    public string ZoneName
    {
        get => zoneName;
        set => zoneName = string.IsNullOrWhiteSpace(value) ? "New Zone" : value.Trim();
    }

    public string AssetBundleName
    {
        get => assetBundleName;
        set => assetBundleName = string.IsNullOrWhiteSpace(value) ? DefaultBundleName() : value.Trim();
    }

    public Vector2 MinBounds
    {
        get => minBounds;
        set => minBounds = value;
    }

    public Vector2 MaxBounds
    {
        get => maxBounds;
        set => maxBounds = value;
    }

    public string EnvironmentTag
    {
        get => environmentTag;
        set => environmentTag = string.IsNullOrWhiteSpace(value) ? "temperate" : value.Trim().ToLowerInvariant();
    }

    public string Notes
    {
        get => notes;
        set => notes = value ?? string.Empty;
    }

    public Vector2 Size
        => new Vector2(Mathf.Max(0f, maxBounds.x - minBounds.x), Mathf.Max(0f, maxBounds.y - minBounds.y));

    public Vector3 TerrainOrigin
        => new Vector3(minBounds.x, 0f, minBounds.y);

    private void Reset()
    {
        zoneName = SceneManager.GetActiveScene().name;
        assetBundleName = DefaultBundleName();
        ApplyStandardBounds(Vector3.zero);
    }

    private void OnValidate()
    {
        zoneId = Math.Max(1, zoneId);
        if (string.IsNullOrWhiteSpace(zoneName))
        {
            zoneName = SceneManager.GetActiveScene().name;
        }

        if (string.IsNullOrWhiteSpace(assetBundleName))
        {
            assetBundleName = DefaultBundleName();
        }

        if (maxBounds.x < minBounds.x)
        {
            maxBounds.x = minBounds.x;
        }

        if (maxBounds.y < minBounds.y)
        {
            maxBounds.y = minBounds.y;
        }

        if ((maxBounds - minBounds).sqrMagnitude <= 0.0001f)
        {
            ApplyStandardBounds(new Vector3(minBounds.x, 0f, minBounds.y));
        }
    }

    public void ApplyStandardBounds(Vector3 terrainOrigin)
    {
        minBounds = new Vector2(terrainOrigin.x, terrainOrigin.z);
        maxBounds = minBounds + new Vector2(StandardTerrainSizeMeters, StandardTerrainSizeMeters);
    }

    private string DefaultBundleName()
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = "zone";
        }

        return sceneName.Trim();
    }
}
}
