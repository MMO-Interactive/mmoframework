using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MMONetworking;

namespace MMONetworking.ServerHost;

public static class Program
{
    public static async Task Main()
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        var runtimeSettings = ZoneRuntimeSettings.Load(workspaceRoot);
        var databasePath = Path.Combine(workspaceRoot, "Documents", "GameplayDefinitions.db");
        var zones = LoadConfiguredZones(databasePath);

        var zoneDirectory = new ZoneDirectory(zones);
        var sessionRegistry = new SessionRegistry();
        var ghostRegistry = new GhostRegistry();
        var gameplayDefinitions = new GameplayDefinitionStore(databasePath, zoneDirectory);
        var accountStore = new AccountStore(databasePath);
        var moderationStore = new ModerationStore(databasePath);
        var inventoryStore = new InventoryStore(databasePath);
        var craftingStore = new CraftingStore(databasePath);
        var shopStore = new ShopStore(databasePath);
        var progressionStore = new CharacterProgressionStore(databasePath);
        var combatStore = new CombatStore(databasePath);
        var reputationStore = new ReputationStore(databasePath);
        var questStore = new QuestStore(databasePath);
        var worldEventStore = new WorldEventStore(databasePath);
        var invasionStore = new InvasionStore(databasePath);
        var gameplayNetworkService = new GameplayNetworkService(
            inventoryStore,
            craftingStore,
            shopStore,
            progressionStore,
            combatStore,
            reputationStore,
            questStore,
            worldEventStore,
            invasionStore,
            gameplayDefinitions);
        var zoneSupervisor = new ZoneSupervisor(zoneDirectory, sessionRegistry, ghostRegistry, runtimeSettings, gameplayNetworkService, gameplayDefinitions);
        var gateway = new GatewayHost(accountStore, zoneDirectory, sessionRegistry, zoneSupervisor, moderationStore, gameplayDefinitions, port: 7000);
        var zoneControl = new ZoneControlHost(zoneDirectory, sessionRegistry, zoneSupervisor, gameplayNetworkService, runtimeSettings, port: runtimeSettings.ZoneControlPort);
        var dashboard = new ManagementDashboardHost(accountStore, gateway, sessionRegistry, zoneSupervisor, gameplayDefinitions, moderationStore, inventoryStore, craftingStore, progressionStore, combatStore, reputationStore, questStore, worldEventStore, invasionStore, port: 7080);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine("MMO networking host starting.");
        Console.WriteLine("Gateway: TCP 7000");
        Console.WriteLine("Dashboard: HTTP 7080");
        Console.WriteLine("Accounts: registration + login required at gateway");
        Console.WriteLine($"Zone control bridge: TCP {runtimeSettings.ZoneControlPort}");
        Console.WriteLine($"Zone lifecycle: on-demand start, stop after {runtimeSettings.IdleShutdownDelay.TotalSeconds:0} seconds idle");
        Console.WriteLine($"Heartbeat: every {runtimeSettings.HeartbeatInterval.TotalSeconds:0}s | session timeout {runtimeSettings.SessionTimeout.TotalSeconds:0}s | reconnect grace {runtimeSettings.ReconnectGracePeriod.TotalSeconds:0}s");
        Console.WriteLine($"AOI radius: {runtimeSettings.AoiRadius:0.##} | ghost margin: {runtimeSettings.GhostMargin:0.##} | prewarm margin: {runtimeSettings.PrewarmMargin:0.##} | transfer inset: {runtimeSettings.TransferInset:0.##}");
        Console.WriteLine($"Mob density: zone 1 -> {runtimeSettings.GetMobCountForZone(1)} | other zones -> {runtimeSettings.DefaultMobCountPerZone}");
        Console.WriteLine($"Configured zones: {string.Join(", ", zones.OrderBy(zone => zone.ZoneId).Select(zone => $"{zone.ZoneId}:{zone.Name}"))}");
        Console.WriteLine(runtimeSettings.UseUnityZoneProcess
            ? $"Zone runtime mode: external Unity process ({runtimeSettings.UnityZoneExecutablePath})"
            : "Zone runtime mode: in-process .NET zone host");
        if (runtimeSettings.UseUnityZoneProcess)
        {
            Console.WriteLine("Warning: external Unity zone runtime currently supports single-zone attach/input/snapshot only.");
            Console.WriteLine("Warning: cross-zone handoff orchestration still lives in the in-process .NET ZoneHost.");
        }

        await Task.WhenAll(
            dashboard.RunAsync(cancellation.Token),
            zoneControl.RunAsync(cancellation.Token),
            gateway.RunAsync(cancellation.Token),
            zoneSupervisor.RunLifecycleLoopAsync(cancellation.Token));
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var hasShared = Directory.Exists(Path.Combine(current.FullName, "Shared", "MMONetworking"));
            var hasServer = Directory.Exists(Path.Combine(current.FullName, "Server", "MMONetworking.ServerHost"));
            var hasClient = Directory.Exists(Path.Combine(current.FullName, "Client"));
            if (hasShared && hasServer && hasClient)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static ZoneDefinition[] LoadConfiguredZones(string databasePath)
    {
        var configuredZones = TryLoadZonesFromGameplayDefinitions(databasePath);
        if (configuredZones.Length > 0)
        {
            return configuredZones;
        }

        return new[]
        {
            CreateZoneDefinition(1, "NorthField", 0f, ZoneDefinition.StandardZoneSizeMeters, 0f, ZoneDefinition.StandardZoneSizeMeters, "zone-1"),
            CreateZoneDefinition(2, "SouthField", ZoneDefinition.StandardZoneSizeMeters, ZoneDefinition.StandardZoneSizeMeters * 2f, 0f, ZoneDefinition.StandardZoneSizeMeters, "zone-2")
        };
    }

    private static ZoneDefinition[] TryLoadZonesFromGameplayDefinitions(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return Array.Empty<ZoneDefinition>();
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM gameplay_definitions WHERE section = 'zones' LIMIT 1;";
        var payload = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<ZoneDefinition>();
        }

        var zones = JsonSerializer.Deserialize<ZoneDefinitionSnapshot[]>(payload, JsonOptions) ?? Array.Empty<ZoneDefinitionSnapshot>();
        return zones
            .OrderBy(zone => zone.ZoneId)
            .Select(zone => CreateZoneDefinition(zone.ZoneId, zone.Name, zone.MinX, zone.MaxX, zone.MinZ, zone.MaxZ, zone.ZoneAssetBundle))
            .ToArray();
    }

    private static ZoneDefinition CreateZoneDefinition(int zoneId, string name, float minX, float maxX, float minZ, float maxZ, string assetBundleName)
        => new(
            zoneId,
            name,
            "127.0.0.1",
            TcpPort: 7100 + zoneId,
            UdpPort: 7200 + zoneId,
            MinX: minX,
            MaxX: maxX,
            MinZ: minZ,
            MaxZ: maxZ,
            AssetBundleName: assetBundleName ?? string.Empty);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
