using RiseOfHeroes.WorldEditor.Authoring;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
internal static class WorldEditorSceneHandleUtility
{
    public static void DrawTerrainSnappedMoveHandle(Transform targetTransform, string label)
    {
        EditorGUI.BeginChangeCheck();
        var nextPosition = Handles.PositionHandle(targetTransform.position, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(targetTransform, label);
            targetTransform.position = WorldEditorTerrainUtility.SnapPositionToTerrain(new Vector3(nextPosition.x, 0f, nextPosition.z));
        }
    }

    public static void DrawZoneBoundsHandle(ZoneSceneMetadata metadata)
    {
        var terrainCenter = metadata.TerrainOrigin + new Vector3(
            ZoneSceneMetadata.StandardTerrainSizeMeters * 0.5f,
            0f,
            ZoneSceneMetadata.StandardTerrainSizeMeters * 0.5f);
        var min = metadata.MinBounds;
        var max = metadata.MaxBounds;
        var center = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
        var size = new Vector3(Mathf.Max(1f, max.x - min.x), 0.1f, Mathf.Max(1f, max.y - min.y));

        Handles.color = new Color(0.45f, 0.85f, 1f, 0.45f);
        Handles.DrawWireCube(
            terrainCenter,
            new Vector3(ZoneSceneMetadata.StandardTerrainSizeMeters, 0.1f, ZoneSceneMetadata.StandardTerrainSizeMeters));

        EditorGUI.BeginChangeCheck();
        var nextBounds = Handles.ScaleHandle(size, center, Quaternion.identity, HandleUtility.GetHandleSize(center));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(metadata, "Resize Zone Bounds");
            var halfX = Mathf.Max(1f, nextBounds.x) * 0.5f;
            var halfZ = Mathf.Max(1f, nextBounds.z) * 0.5f;
            metadata.MinBounds = new Vector2(center.x - halfX, center.z - halfZ);
            metadata.MaxBounds = new Vector2(center.x + halfX, center.z + halfZ);
            EditorUtility.SetDirty(metadata);
        }
    }
}
}
