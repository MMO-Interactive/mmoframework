using System;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoMinimapView : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private UnityMmoRemoteAvatarSystem remoteAvatarSystem;
    [SerializeField] private UnityMmoResourceNodeSystem resourceNodeSystem;
    [SerializeField] private UnityMmoMobSystem mobSystem;
    [SerializeField] private UnityMmoNpcSystem npcSystem;
    [SerializeField] private Vector2 margin = new Vector2(16f, 16f);
    [SerializeField] private float size = 220f;
    [SerializeField] private float worldRadius = 40f;

    private void Awake()
    {
        if (client == null)
        {
            client = FindObjectOfType<UnityMmoClient>();
        }

        if (remoteAvatarSystem == null)
        {
            remoteAvatarSystem = FindObjectOfType<UnityMmoRemoteAvatarSystem>();
        }

        if (resourceNodeSystem == null)
        {
            resourceNodeSystem = FindObjectOfType<UnityMmoResourceNodeSystem>();
        }

        if (mobSystem == null)
        {
            mobSystem = FindObjectOfType<UnityMmoMobSystem>();
        }

        if (npcSystem == null)
        {
            npcSystem = FindObjectOfType<UnityMmoNpcSystem>();
        }
    }

    private void OnGUI()
    {
        if (client == null || !client.IsConnected)
        {
            return;
        }

        var rect = new Rect(
            margin.x,
            Mathf.Max(margin.y, Screen.height - (size + 34f) - margin.y),
            size,
            size + 34f);
        GUI.Box(rect, "Minimap");

        var mapRect = new Rect(rect.x + 10f, rect.y + 24f, size - 20f, size - 20f);
        var previousColor = GUI.color;
        GUI.color = new Color(0.08f, 0.12f, 0.16f, 0.94f);
        GUI.Box(mapRect, string.Empty);
        GUI.color = previousColor;

        DrawGrid(mapRect);
        DrawPlayerMarker(mapRect, client.AuthoritativePosition, new Color(0.32f, 0.68f, 1f), 8f, true);

        if (remoteAvatarSystem != null)
        {
            foreach (var remote in remoteAvatarSystem.GetVisibleRemotePlayers())
            {
                var color = remote.Kind == SnapshotEntityKind.Ghost
                    ? new Color(0.68f, 0.48f, 0.96f)
                    : new Color(0.96f, 0.58f, 0.28f);
                DrawPlayerMarker(mapRect, new Vector3(remote.Position.X, remote.Position.Y, remote.Position.Z), color, 6f, false);
            }
        }

        if (resourceNodeSystem != null)
        {
            foreach (var node in resourceNodeSystem.GetVisibleNodes())
            {
                var color = ResolveNodeColor(node.ResourceId);
                DrawSquareMarker(mapRect, new Vector3(node.Position.X, node.Position.Y, node.Position.Z), color, 5f);
            }
        }

        if (mobSystem != null)
        {
            foreach (var mob in mobSystem.GetVisibleMobs())
            {
                var color = ResolveMobColor(mob.MobTypeId);
                DrawDiamondMarker(mapRect, new Vector3(mob.Position.X, mob.Position.Y, mob.Position.Z), color, 7f);
            }
        }

        if (npcSystem != null)
        {
            foreach (var npc in npcSystem.GetVisibleNpcs())
            {
                DrawTriangleMarker(mapRect, new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z), ResolveNpcColor(npc.PrimaryRole, npc.Services, npc.ServiceOptions), 7f);
            }
        }

        GUI.Label(new Rect(rect.x + 12f, rect.y + rect.height - 18f, rect.width - 24f, 16f), "40m radar | blue self | orange players | purple ghosts | gold npcs");
    }

    private void DrawGrid(Rect mapRect)
    {
        DrawLine(new Vector2(mapRect.center.x, mapRect.y + 6f), new Vector2(mapRect.center.x, mapRect.yMax - 6f), new Color(1f, 1f, 1f, 0.14f), 1f);
        DrawLine(new Vector2(mapRect.x + 6f, mapRect.center.y), new Vector2(mapRect.xMax - 6f, mapRect.center.y), new Color(1f, 1f, 1f, 0.14f), 1f);
        GUI.Label(new Rect(mapRect.x + 6f, mapRect.y + 4f, 40f, 16f), "N");
        GUI.Label(new Rect(mapRect.xMax - 14f, mapRect.center.y - 8f, 16f, 16f), "E");
        GUI.Label(new Rect(mapRect.center.x - 6f, mapRect.yMax - 18f, 16f, 16f), "S");
        GUI.Label(new Rect(mapRect.x + 4f, mapRect.center.y - 8f, 16f, 16f), "W");
    }

    private void DrawPlayerMarker(Rect mapRect, Vector3 worldPosition, Color color, float sizePixels, bool self)
    {
        var point = ProjectToMap(mapRect, worldPosition);
        if (!mapRect.Contains(point))
        {
            return;
        }

        var previousColor = GUI.color;
        GUI.color = color;
        GUI.Box(new Rect(point.x - (sizePixels * 0.5f), point.y - (sizePixels * 0.5f), sizePixels, sizePixels), string.Empty);
        GUI.color = previousColor;

        if (self)
        {
            DrawLine(point + new Vector2(0f, -8f), point + new Vector2(0f, -16f), color, 2f);
        }
    }

    private void DrawSquareMarker(Rect mapRect, Vector3 worldPosition, Color color, float sizePixels)
    {
        var point = ProjectToMap(mapRect, worldPosition);
        if (!mapRect.Contains(point))
        {
            return;
        }

        var previousColor = GUI.color;
        GUI.color = color;
        GUI.Box(new Rect(point.x - (sizePixels * 0.5f), point.y - (sizePixels * 0.5f), sizePixels, sizePixels), string.Empty);
        GUI.color = previousColor;
    }

    private void DrawDiamondMarker(Rect mapRect, Vector3 worldPosition, Color color, float sizePixels)
    {
        var point = ProjectToMap(mapRect, worldPosition);
        if (!mapRect.Contains(point))
        {
            return;
        }

        DrawLine(point + new Vector2(0f, -sizePixels), point + new Vector2(sizePixels, 0f), color, 2f);
        DrawLine(point + new Vector2(sizePixels, 0f), point + new Vector2(0f, sizePixels), color, 2f);
        DrawLine(point + new Vector2(0f, sizePixels), point + new Vector2(-sizePixels, 0f), color, 2f);
        DrawLine(point + new Vector2(-sizePixels, 0f), point + new Vector2(0f, -sizePixels), color, 2f);
    }

    private void DrawTriangleMarker(Rect mapRect, Vector3 worldPosition, Color color, float sizePixels)
    {
        var point = ProjectToMap(mapRect, worldPosition);
        if (!mapRect.Contains(point))
        {
            return;
        }

        DrawLine(point + new Vector2(0f, -sizePixels), point + new Vector2(sizePixels, sizePixels * 0.6f), color, 2f);
        DrawLine(point + new Vector2(sizePixels, sizePixels * 0.6f), point + new Vector2(-sizePixels, sizePixels * 0.6f), color, 2f);
        DrawLine(point + new Vector2(-sizePixels, sizePixels * 0.6f), point + new Vector2(0f, -sizePixels), color, 2f);
    }

    private Vector2 ProjectToMap(Rect mapRect, Vector3 worldPosition)
    {
        var delta = worldPosition - client.AuthoritativePosition;
        var normalizedX = Mathf.Clamp(delta.x / worldRadius, -1f, 1f);
        var normalizedZ = Mathf.Clamp(delta.z / worldRadius, -1f, 1f);
        return new Vector2(
            mapRect.center.x + normalizedX * (mapRect.width * 0.5f),
            mapRect.center.y - normalizedZ * (mapRect.height * 0.5f));
    }

    private static Color ResolveNodeColor(string resourceId)
    {
        if (!string.IsNullOrWhiteSpace(resourceId) && resourceId.IndexOf("ore", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.72f, 0.76f, 0.86f);
        }

        return new Color(0.36f, 0.84f, 0.52f);
    }

    private static Color ResolveMobColor(string mobTypeId)
    {
        if (!string.IsNullOrWhiteSpace(mobTypeId) && mobTypeId.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.96f, 0.48f, 0.54f);
        }

        return new Color(0.82f, 0.42f, 0.28f);
    }

    private static Color ResolveNpcColor(string primaryRole, string[] services, NpcServiceSnapshot[] serviceOptions)
    {
        if (HasNpcAction(serviceOptions, "quests"))
        {
            return new Color(0.42f, 0.88f, 0.62f);
        }

        if (!string.IsNullOrWhiteSpace(primaryRole) &&
            primaryRole.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.42f, 0.88f, 0.62f);
        }

        if (services != null)
        {
            for (var i = 0; i < services.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(services[i]) &&
                    services[i].IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new Color(0.42f, 0.88f, 0.62f);
                }
            }
        }

        if (HasNpcAction(serviceOptions, "training"))
        {
            return new Color(0.43f, 0.67f, 0.96f);
        }

        if (!string.IsNullOrWhiteSpace(primaryRole) &&
            primaryRole.IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new Color(0.43f, 0.67f, 0.96f);
        }

        if (services != null)
        {
            for (var i = 0; i < services.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(services[i]) &&
                    (services[i].IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     services[i].IndexOf("progress", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return new Color(0.43f, 0.67f, 0.96f);
                }
            }
        }

        return new Color(0.94f, 0.78f, 0.36f);
    }

    private static bool HasNpcAction(NpcServiceSnapshot[] serviceOptions, string actionId)
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

    private static Texture2D _lineTexture;

    private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
        if (_lineTexture == null)
        {
            _lineTexture = new Texture2D(1, 1);
            _lineTexture.SetPixel(0, 0, Color.white);
            _lineTexture.Apply();
        }

        var matrix = GUI.matrix;
        var previousColor = GUI.color;
        var angle = Vector3.Angle(b - a, Vector2.right);
        if (a.y > b.y)
        {
            angle = -angle;
        }

        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - (width * 0.5f), (b - a).magnitude, width), _lineTexture);
        GUI.matrix = matrix;
        GUI.color = previousColor;
    }
}
}
