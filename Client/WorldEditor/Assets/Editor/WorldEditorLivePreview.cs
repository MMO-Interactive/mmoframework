using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RiseOfHeroes.WorldEditor
{
[InitializeOnLoad]
public static class WorldEditorLivePreview
{
    private static LiveDashboardSnapshot _dashboard;
    private static LiveGameplayDefinitionsSnapshot _definitions;
    private static int _zoneId;
    private static bool _enabled;

    static WorldEditorLivePreview()
    {
        SceneView.duringSceneGui += OnSceneGui;
    }

    public static void SetData(bool enabled, int zoneId, LiveDashboardSnapshot dashboard, LiveGameplayDefinitionsSnapshot definitions)
    {
        _enabled = enabled;
        _zoneId = zoneId;
        _dashboard = dashboard;
        _definitions = definitions;
        SceneView.RepaintAll();
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (!_enabled || _zoneId <= 0 || sceneView.camera == null)
        {
            return;
        }

        var zone = _dashboard?.zones?.FirstOrDefault(entry => entry.zoneId == _zoneId);
        var nodes = _definitions?.nodes?.Where(entry => entry.zoneId == _zoneId).ToArray() ?? Array.Empty<LiveResourceNodeDefinition>();
        var npcs = _definitions?.npcs?.Where(entry => entry.zoneId == _zoneId).ToArray() ?? Array.Empty<LiveNpcDefinition>();
        var playerSpawns = _definitions?.playerSpawns?.Where(entry => entry.zoneId == _zoneId).ToArray() ?? Array.Empty<LivePlayerSpawnDefinition>();
        var mobSpawns = _definitions?.mobSpawns?.Where(entry => entry.zoneId == _zoneId).ToArray() ?? Array.Empty<LiveMobSpawnDefinition>();

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        DrawPlayerSpawns(playerSpawns);
        DrawMobSpawns(mobSpawns);
        DrawNodes(nodes);
        DrawNpcs(npcs);
        DrawPlayers(zone);
        DrawMobs(zone);
    }

    private static void DrawPlayers(LiveZoneRuntimeSnapshot zone)
    {
        if (zone?.players == null)
        {
            return;
        }

        Handles.color = new Color(0.2f, 0.7f, 1f, 0.95f);
        foreach (var player in zone.players)
        {
            var position = new Vector3(player.positionX, player.positionY + 1.2f, player.positionZ);
            Handles.SphereHandleCap(0, position, Quaternion.identity, 0.65f, EventType.Repaint);
            Handles.Label(position + Vector3.up * 0.45f, "P " + player.playerId);
        }
    }

    private static void DrawMobs(LiveZoneRuntimeSnapshot zone)
    {
        if (zone?.mobs == null)
        {
            return;
        }

        Handles.color = new Color(1f, 0.4f, 0.35f, 0.95f);
        foreach (var mob in zone.mobs)
        {
            var position = new Vector3(mob.positionX, mob.positionY + 1f, mob.positionZ);
            Handles.CubeHandleCap(0, position, Quaternion.identity, 0.7f, EventType.Repaint);
            Handles.Label(position + Vector3.up * 0.45f, mob.mobTypeId + " [" + mob.state + "]");
        }
    }

    private static void DrawNodes(LiveResourceNodeDefinition[] nodes)
    {
        Handles.color = new Color(0.3f, 0.95f, 0.55f, 0.95f);
        foreach (var node in nodes)
        {
            var position = new Vector3(node.positionX, node.positionY + 0.6f, node.positionZ);
            Handles.DrawWireDisc(position, Vector3.up, 0.55f);
            Handles.Label(position + Vector3.up * 0.25f, node.resourceId + " • " + node.id);
        }
    }

    private static void DrawPlayerSpawns(LivePlayerSpawnDefinition[] spawns)
    {
        Handles.color = new Color(0.35f, 0.8f, 1f, 0.95f);
        foreach (var spawn in spawns)
        {
            var position = new Vector3(spawn.positionX, spawn.positionY + 0.15f, spawn.positionZ);
            Handles.DrawWireDisc(position, Vector3.up, 1f);
            var direction = Quaternion.Euler(0f, spawn.facingYaw, 0f) * Vector3.forward;
            Handles.ArrowHandleCap(0, position, Quaternion.LookRotation(direction, Vector3.up), 2.5f, EventType.Repaint);
            Handles.Label(position + Vector3.up * 0.25f, "spawn • " + (string.IsNullOrWhiteSpace(spawn.spawnTag) ? spawn.spawnId : spawn.spawnTag));
        }
    }

    private static void DrawMobSpawns(LiveMobSpawnDefinition[] spawns)
    {
        Handles.color = new Color(1f, 0.55f, 0.25f, 0.95f);
        foreach (var spawn in spawns)
        {
            var position = new Vector3(spawn.positionX, spawn.positionY + 0.1f, spawn.positionZ);
            Handles.DrawWireDisc(position, Vector3.up, Mathf.Max(0.5f, spawn.radius));
            Handles.color = new Color(1f, 0.7f, 0.3f, 0.55f);
            Handles.DrawWireDisc(position, Vector3.up, Mathf.Max(Mathf.Max(0.5f, spawn.radius), spawn.roamRadius));
            Handles.color = new Color(1f, 0.55f, 0.25f, 0.95f);
            Handles.Label(position + Vector3.up * 0.25f, spawn.mobTypeId + " x" + spawn.count + " • " + spawn.spawnId);
        }
    }

    private static void DrawNpcs(LiveNpcDefinition[] npcs)
    {
        Handles.color = new Color(1f, 0.84f, 0.28f, 0.95f);
        foreach (var npc in npcs)
        {
            var position = new Vector3(npc.positionX, npc.positionY + 1.3f, npc.positionZ);
            Handles.CylinderHandleCap(0, position, Quaternion.identity, 0.9f, EventType.Repaint);
            Handles.Label(position + Vector3.up * 0.45f, npc.displayName + " • " + npc.primaryRole);
        }
    }
}
}
