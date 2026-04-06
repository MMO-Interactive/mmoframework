using System;
using System.Linq;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoWorldTrackerView : MonoBehaviour
{
    [SerializeField] private UnityMmoClient client;
    [SerializeField] private UnityMmoResourceNodeSystem resourceNodeSystem;
    [SerializeField] private UnityMmoMobSystem mobSystem;
    [SerializeField] private UnityMmoNpcSystem npcSystem;
    [SerializeField] private UnityMmoRemoteAvatarSystem remoteAvatarSystem;
    [SerializeField] private Vector2 questOrigin = new Vector2(1010f, 150f);
    [SerializeField] private Vector2 nearbyOrigin = new Vector2(1010f, 390f);
    [SerializeField] private float nearbyRadius = 18f;

    private void Awake()
    {
        if (client == null)
        {
            client = FindObjectOfType<UnityMmoClient>();
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

        if (remoteAvatarSystem == null)
        {
            remoteAvatarSystem = FindObjectOfType<UnityMmoRemoteAvatarSystem>();
        }
    }

    private void OnGUI()
    {
        if (client == null || !client.IsConnected)
        {
            return;
        }

        DrawQuestTracker();
        DrawNearbySummary();
    }

    private void DrawQuestTracker()
    {
        var rect = new Rect(questOrigin.x, questOrigin.y, 320f, 220f);
        GUI.Box(rect, "Tracker");

        var y = rect.y + 28f;
        var questBoard = client.QuestBoard;
        if (questBoard != null)
        {
            var questCount = 0;
            foreach (var definition in questBoard.definitions ?? Array.Empty<QuestDefinitionData>())
            {
                var progress = (questBoard.progress ?? Array.Empty<QuestProgressData>()).FirstOrDefault(x => x.questId == definition.questId);
                if (progress != null && progress.isClaimed)
                {
                    continue;
                }

                var amount = progress != null ? progress.progressCount : 0;
                var completed = progress != null && progress.isCompleted;
                var line = completed
                    ? definition.title + "  complete - claim reward"
                    : definition.title + "  " + amount + "/" + definition.targetCount;
                GUI.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 20f), line);
                y += 22f;
                questCount++;
                if (questCount >= 4)
                {
                    break;
                }
            }

            if (questCount == 0)
            {
                GUI.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 20f), "No active quests tracked.");
                y += 22f;
            }
        }
        else
        {
            GUI.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 20f), "Quest board not loaded yet.");
            y += 22f;
        }

        var worldEvents = client.WorldEvents;
        if (worldEvents != null)
        {
            var activeEvent = (worldEvents.events ?? Array.Empty<WorldEventData>())
                .FirstOrDefault(evt => string.Equals(evt.state, "Active", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(evt.state, "Running", StringComparison.OrdinalIgnoreCase));
            if (activeEvent != null)
            {
                GUI.Label(new Rect(rect.x + 14f, y + 10f, rect.width - 28f, 20f), "Event: " + activeEvent.title + " [" + activeEvent.state + "]");
                y += 22f;
            }
        }

        var invasions = client.Invasions;
        if (invasions != null)
        {
            var zoneInvasion = (invasions.invasions ?? Array.Empty<InvasionData>())
                .FirstOrDefault(inv => inv.zoneId == client.CurrentZoneId && !string.Equals(inv.state, "Completed", StringComparison.OrdinalIgnoreCase));
            if (zoneInvasion != null)
            {
                GUI.Label(new Rect(rect.x + 14f, y + 10f, rect.width - 28f, 20f), "Invasion: wave " + zoneInvasion.currentWave + "/" + zoneInvasion.totalWaves + " threat " + zoneInvasion.threatLevel);
            }
        }
    }

    private void DrawNearbySummary()
    {
        var rect = new Rect(nearbyOrigin.x, nearbyOrigin.y, 320f, 178f);
        GUI.Box(rect, "Nearby");

        var playerPosition = client.AuthoritativePosition;
        var nearbyNodes = resourceNodeSystem != null
            ? resourceNodeSystem.GetVisibleNodes().Count(node => DistanceSq(node.Position, playerPosition) <= nearbyRadius * nearbyRadius)
            : 0;
        var nearbyMobs = mobSystem != null
            ? mobSystem.GetVisibleMobs().Count(mob => DistanceSq(mob.Position, playerPosition) <= nearbyRadius * nearbyRadius)
            : 0;
        var nearbyPlayers = remoteAvatarSystem != null
            ? remoteAvatarSystem.GetVisibleRemotePlayers().Count(remote => DistanceSq(remote.Position, playerPosition) <= nearbyRadius * nearbyRadius)
            : 0;
        var nearbyNpcs = npcSystem != null
            ? npcSystem.GetVisibleNpcs().Count(npc => DistanceSq(npc.Position, playerPosition) <= nearbyRadius * nearbyRadius)
            : 0;

        GUI.Label(new Rect(rect.x + 14f, rect.y + 30f, rect.width - 28f, 20f), "Within " + nearbyRadius.ToString("F0") + "m");
        GUI.Label(new Rect(rect.x + 14f, rect.y + 56f, rect.width - 28f, 20f), "Resource nodes: " + nearbyNodes);
        GUI.Label(new Rect(rect.x + 14f, rect.y + 78f, rect.width - 28f, 20f), "Mobs: " + nearbyMobs);
        GUI.Label(new Rect(rect.x + 14f, rect.y + 100f, rect.width - 28f, 20f), "NPCs: " + nearbyNpcs);
        GUI.Label(new Rect(rect.x + 14f, rect.y + 122f, rect.width - 28f, 20f), "Other players: " + nearbyPlayers);

        var progression = client.Progression;
        if (progression != null)
        {
            var topTrack = (progression.tracks ?? Array.Empty<ProgressionTrackData>())
                .OrderByDescending(track => track.level)
                .ThenByDescending(track => track.experience)
                .FirstOrDefault();
            if (topTrack != null)
            {
                GUI.Label(new Rect(rect.x + 14f, rect.y + 144f, rect.width - 28f, 20f), "Top skill: " + topTrack.trackId + " lvl " + topTrack.level);
            }
        }
    }

    private static float DistanceSq(NetworkVector3 snapshotPosition, Vector3 playerPosition)
    {
        var dx = snapshotPosition.X - playerPosition.x;
        var dz = snapshotPosition.Z - playerPosition.z;
        return (dx * dx) + (dz * dz);
    }
}
}
