using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
public static class WorldEditorSceneGizmos
{
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawZoneMetadataGizmo(ZoneSceneMetadata metadata, GizmoType gizmoType)
    {
        var center = new Vector3((metadata.MinBounds.x + metadata.MaxBounds.x) * 0.5f, 0f, (metadata.MinBounds.y + metadata.MaxBounds.y) * 0.5f);
        var size = new Vector3(metadata.MaxBounds.x - metadata.MinBounds.x, 0.1f, metadata.MaxBounds.y - metadata.MinBounds.y);
        Gizmos.color = new Color(0.22f, 0.69f, 0.95f, 0.35f);
        Gizmos.DrawWireCube(center, size);

        Gizmos.color = new Color(0.95f, 0.95f, 0.25f, 0.20f);
        Gizmos.DrawWireCube(
            metadata.TerrainOrigin + new Vector3(
                ZoneSceneMetadata.StandardTerrainSizeMeters * 0.5f,
                0f,
                ZoneSceneMetadata.StandardTerrainSizeMeters * 0.5f),
            new Vector3(ZoneSceneMetadata.StandardTerrainSizeMeters, 0.1f, ZoneSceneMetadata.StandardTerrainSizeMeters));

        Handles.Label(
            center + Vector3.up * 1.5f,
            metadata.ZoneName + " (" + metadata.ZoneId + ") | " + metadata.Size.x.ToString("F0") + "m x " + metadata.Size.y.ToString("F0") + "m");
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawPlayerSpawnGizmo(PlayerSpawnMarker marker, GizmoType gizmoType)
    {
        Gizmos.color = new Color(0.30f, 0.75f, 1f, 0.8f);
        Gizmos.DrawSphere(marker.transform.position + Vector3.up * 0.5f, 0.35f);
        Handles.Label(marker.transform.position + Vector3.up * 1.1f, marker.SpawnId);
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawNpcGizmo(NpcSpawnMarker marker, GizmoType gizmoType)
    {
        Gizmos.color = new Color(1f, 0.84f, 0.30f, 0.85f);
        Gizmos.DrawCube(marker.transform.position + Vector3.up * 1.0f, new Vector3(0.6f, 1.8f, 0.6f));
        Handles.Label(marker.transform.position + Vector3.up * 2.0f, marker.DisplayName + " | " + marker.PrimaryRole);
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawResourceGizmo(ResourceNodeMarker marker, GizmoType gizmoType)
    {
        Gizmos.color = new Color(0.25f, 0.92f, 0.60f, 0.85f);
        Gizmos.DrawWireCube(marker.transform.position + Vector3.up * 0.5f, Vector3.one * 0.75f);
        Handles.Label(marker.transform.position + Vector3.up * 1.1f, marker.ResourceId);
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawMobGizmo(MobSpawnMarker marker, GizmoType gizmoType)
    {
        Gizmos.color = new Color(1f, 0.42f, 0.35f, 0.9f);
        Gizmos.DrawWireSphere(marker.transform.position, marker.Radius);
        Gizmos.color = new Color(1f, 0.42f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(marker.transform.position, marker.RoamRadius);
        Handles.Label(marker.transform.position + Vector3.up * 1.2f, marker.MobTypeId + " x" + marker.Count);
    }
}
}
