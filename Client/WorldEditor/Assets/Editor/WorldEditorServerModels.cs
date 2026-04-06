using System;

namespace RiseOfHeroes.WorldEditor
{
[Serializable]
public sealed class LiveDashboardSnapshot
{
    public string generatedAtUtc;
    public LiveZoneRuntimeSnapshot[] zones = Array.Empty<LiveZoneRuntimeSnapshot>();
}

[Serializable]
public sealed class LiveZoneRuntimeSnapshot
{
    public int zoneId;
    public string name;
    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;
    public string runtimeMode;
    public string lifecycleState;
    public uint tick;
    public int activePlayers;
    public LiveZonePlayerSnapshot[] players = Array.Empty<LiveZonePlayerSnapshot>();
    public LiveZoneMobSnapshot[] mobs = Array.Empty<LiveZoneMobSnapshot>();
}

[Serializable]
public sealed class LiveZonePlayerSnapshot
{
    public string sessionId;
    public ulong playerId;
    public float positionX;
    public float positionY;
    public float positionZ;
    public float velocityX;
    public float velocityY;
    public float velocityZ;
}

[Serializable]
public sealed class LiveZoneMobSnapshot
{
    public string mobId;
    public string mobTypeId;
    public float positionX;
    public float positionY;
    public float positionZ;
    public float velocityX;
    public float velocityY;
    public float velocityZ;
    public string state;
}

[Serializable]
public sealed class LiveGameplayDefinitionsSnapshot
{
    public LiveItemDefinition[] items = Array.Empty<LiveItemDefinition>();
    public LiveSkillDefinition[] skills = Array.Empty<LiveSkillDefinition>();
    public LiveResourceDefinition[] resources = Array.Empty<LiveResourceDefinition>();
    public LiveResourceNodeDefinition[] nodes = Array.Empty<LiveResourceNodeDefinition>();
    public LiveZoneDefinition[] zones = Array.Empty<LiveZoneDefinition>();
    public LiveNpcDefinition[] npcs = Array.Empty<LiveNpcDefinition>();
    public LivePlayerSpawnDefinition[] playerSpawns = Array.Empty<LivePlayerSpawnDefinition>();
    public LiveMobSpawnDefinition[] mobSpawns = Array.Empty<LiveMobSpawnDefinition>();
}

[Serializable]
public sealed class LiveItemDefinition
{
    public string id;
    public string name;
    public int maxStack;
    public float baseWeight;
}

[Serializable]
public sealed class LiveSkillDefinition
{
    public string id;
    public string name;
    public int maxValue;
}

[Serializable]
public sealed class LiveResourceDefinition
{
    public string id;
    public string name;
    public string itemId;
    public int baseYield;
}

[Serializable]
public sealed class LiveResourceNodeDefinition
{
    public string id;
    public int zoneId;
    public string resourceId;
    public float positionX;
    public float positionY;
    public float positionZ;
    public int respawnSeconds;
}

[Serializable]
public sealed class LiveZoneDefinition
{
    public int zoneId;
    public string name;
    public string zoneAssetBundle;
    public float minX;
    public float maxX;
    public float minZ;
    public float maxZ;
}

[Serializable]
public sealed class LiveNpcServiceDefinition
{
    public string actionId;
    public string label;
    public string uiHint;
}

[Serializable]
public sealed class LiveNpcDefinition
{
    public string npcId;
    public int zoneId;
    public string npcTypeId;
    public string displayName;
    public float positionX;
    public float positionY;
    public float positionZ;
    public string primaryRole;
    public string[] services = Array.Empty<string>();
    public string greetingText;
    public LiveNpcServiceDefinition[] serviceOptions = Array.Empty<LiveNpcServiceDefinition>();
}

[Serializable]
public sealed class LivePlayerSpawnDefinition
{
    public string spawnId;
    public int zoneId;
    public float positionX;
    public float positionY;
    public float positionZ;
    public bool isDefaultSpawn;
    public float facingYaw;
    public string spawnTag;
}

[Serializable]
public sealed class LiveMobSpawnDefinition
{
    public string spawnId;
    public int zoneId;
    public string mobTypeId;
    public float positionX;
    public float positionY;
    public float positionZ;
    public int count;
    public float radius;
    public float roamRadius;
}

[Serializable]
public sealed class AssetUploadResponse
{
    public string assetType;
    public string sourceName;
    public object upload;
    public object registration;
    public object manifestRegistration;
}

[Serializable]
public sealed class AssetChannelManifestSnapshot
{
    public string channel;
    public long manifestId;
    public string generatedAtUtc;
    public AssetChannelManifestEntry[] entries = Array.Empty<AssetChannelManifestEntry>();
}

[Serializable]
public sealed class AssetChannelManifestEntry
{
    public string bundleName;
    public string platform;
    public string version;
    public long bundleVersionId;
    public string artifactHash;
    public string unityVersion;
    public string downloadUrl;
}
}
