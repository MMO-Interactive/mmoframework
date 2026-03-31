using System.Threading.Tasks;
using System;
using UnityEngine;

namespace MMONetworking.ZoneServer
{
public sealed class UnityZoneServerBootstrap : MonoBehaviour
{
    [Header("Zone")]
    [SerializeField] private int zoneId = 1;
    [SerializeField] private string zoneName = "UnityZone";
    [SerializeField] private string host = "127.0.0.1";
    [SerializeField] private int tcpPort = 7301;
    [SerializeField] private int udpPort = 7401;
    [SerializeField] private float minX = 0f;
    [SerializeField] private float maxX = 100f;
    [SerializeField] private float minZ = 0f;
    [SerializeField] private float maxZ = 100f;
    [SerializeField] private float prewarmMargin = 20f;
    [SerializeField] private int mobCount = 2;
    [Header("Control Plane")]
    [SerializeField] private string controlHost = "127.0.0.1";
    [SerializeField] private int controlPort = 7050;
    [SerializeField] private bool startOnAwake = true;

    private UnityZoneServerRuntime _runtime;
    private Task _runTask;
    private string _status = "Idle";

    public string Status => _status;

    private async void Awake()
    {
        ApplyCommandLineOverrides();
        if (startOnAwake)
        {
            await StartServerAsync();
        }
    }

    private void OnDestroy()
    {
        _runtime?.Dispose();
        _runtime = null;
    }

    public async Task StartServerAsync()
    {
        if (_runtime != null)
        {
            return;
        }

        var definition = new ZoneDefinition(zoneId, zoneName, host, tcpPort, udpPort, minX, maxX, minZ, maxZ);
        _runtime = new UnityZoneServerRuntime(definition, controlHost, controlPort, prewarmMargin, mobCount);
        _status = "Starting";
        _runTask = _runtime.RunAsync();
        await Task.Yield();
        _status = $"Listening TCP {tcpPort} / UDP {udpPort}";
    }

    private void Update()
    {
        if (_runtime != null)
        {
            _status = _runtime.Summary;
        }
    }

    private void ApplyCommandLineOverrides()
    {
        var args = Environment.GetCommandLineArgs();
        zoneId = ReadInt(args, "-mmo-zone-id", zoneId);
        zoneName = ReadString(args, "-mmo-zone-name", zoneName);
        host = ReadString(args, "-mmo-host", host);
        tcpPort = ReadInt(args, "-mmo-tcp-port", tcpPort);
        udpPort = ReadInt(args, "-mmo-udp-port", udpPort);
        minX = ReadFloat(args, "-mmo-min-x", minX);
        maxX = ReadFloat(args, "-mmo-max-x", maxX);
        minZ = ReadFloat(args, "-mmo-min-z", minZ);
        maxZ = ReadFloat(args, "-mmo-max-z", maxZ);
        prewarmMargin = ReadFloat(args, "-mmo-prewarm-margin", prewarmMargin);
        mobCount = ReadInt(args, "-mmo-mob-count", mobCount);
        controlHost = ReadString(args, "-mmo-control-host", controlHost);
        controlPort = ReadInt(args, "-mmo-control-port", controlPort);
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var value = ReadString(args, name, null);
        int parsed;
        return value != null && int.TryParse(value, out parsed) ? parsed : fallback;
    }

    private static float ReadFloat(string[] args, string name, float fallback)
    {
        var value = ReadString(args, name, null);
        float parsed;
        return value != null && float.TryParse(value, out parsed) ? parsed : fallback;
    }

    private static string ReadString(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }
}
}
