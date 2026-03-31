using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public static class Program
{
    public static async Task Main()
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        var runtimeSettings = ZoneRuntimeSettings.Load(workspaceRoot);
        var zones = new[]
        {
            new ZoneDefinition(1, "NorthField", "127.0.0.1", TcpPort: 7101, UdpPort: 7201, MinX: 0f, MaxX: 100f, MinZ: 0f, MaxZ: 100f),
            new ZoneDefinition(2, "SouthField", "127.0.0.1", TcpPort: 7102, UdpPort: 7202, MinX: 100f, MaxX: 200f, MinZ: 0f, MaxZ: 100f)
        };

        var zoneDirectory = new ZoneDirectory(zones);
        var sessionRegistry = new SessionRegistry();
        var ghostRegistry = new GhostRegistry();
        var zoneSupervisor = new ZoneSupervisor(zoneDirectory, sessionRegistry, ghostRegistry, runtimeSettings);
        var gateway = new GatewayHost(zoneDirectory, sessionRegistry, zoneSupervisor, port: 7000);
        var zoneControl = new ZoneControlHost(zoneDirectory, sessionRegistry, zoneSupervisor, runtimeSettings, port: runtimeSettings.ZoneControlPort);
        var gameplayDefinitions = new GameplayDefinitionStore(Path.Combine(workspaceRoot, "Documents", "GameplayDefinitions.json"), zoneDirectory);
        var dashboard = new ManagementDashboardHost(gateway, sessionRegistry, zoneSupervisor, gameplayDefinitions, port: 7080);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };

        Console.WriteLine("MMO networking host starting.");
        Console.WriteLine("Gateway: TCP 7000");
        Console.WriteLine("Dashboard: HTTP 7080");
        Console.WriteLine($"Zone control bridge: TCP {runtimeSettings.ZoneControlPort}");
        Console.WriteLine($"Zone lifecycle: on-demand start, stop after {runtimeSettings.IdleShutdownDelay.TotalSeconds:0} seconds idle");
        Console.WriteLine($"AOI radius: {runtimeSettings.AoiRadius:0.##} | ghost margin: {runtimeSettings.GhostMargin:0.##} | prewarm margin: {runtimeSettings.PrewarmMargin:0.##} | transfer inset: {runtimeSettings.TransferInset:0.##}");
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
}
