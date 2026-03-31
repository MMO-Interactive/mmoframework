using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MMONetworking;

namespace MMONetworking.ServerHost;

public sealed class ExternalUnityZoneProcessHost : IZoneRuntimeHost
{
    private readonly ZoneDefinition _definition;
    private readonly ZoneRuntimeSettings _settings;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<Guid, ExternalPlayerState> _players = new();
    private volatile MobSnapshot[] _mobs = Array.Empty<MobSnapshot>();
    private Process? _process;

    public ExternalUnityZoneProcessHost(ZoneDefinition definition, ZoneRuntimeSettings settings)
    {
        _definition = definition;
        _settings = settings;
    }

    public int ActivePlayerCount => _players.Count;
    public Task Ready => _ready.Task;
    public string RuntimeMode => "UnityProcess";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var executablePath = _settings.UnityZoneExecutablePath;
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Unity zone server executable was not found.", executablePath);
        }

        EnsurePortsAvailable();

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = string.Join(' ', new[]
            {
                "-batchmode",
                "-nographics",
                "-mmo-control-host 127.0.0.1",
                $"-mmo-control-port {_settings.ZoneControlPort}",
                $"-mmo-zone-id {_definition.ZoneId}",
                $"-mmo-zone-name \"{_definition.Name}\"",
                $"-mmo-host {_definition.Host}",
                $"-mmo-tcp-port {_definition.TcpPort}",
                $"-mmo-udp-port {_definition.UdpPort}",
                $"-mmo-min-x {_definition.MinX}",
                $"-mmo-max-x {_definition.MaxX}",
                $"-mmo-min-z {_definition.MinZ}",
                $"-mmo-max-z {_definition.MaxZ}",
                $"-mmo-prewarm-margin {_settings.PrewarmMargin}",
                $"-mmo-mob-count {_settings.GetMobCountForZone(_definition.ZoneId)}"
            })
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            if (!_ready.Task.IsCompleted)
            {
                _ready.TrySetException(new InvalidOperationException($"Unity zone process for zone {_definition.ZoneId} exited before becoming ready."));
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException($"Failed to start Unity zone process for zone {_definition.ZoneId}.");
        }

        _process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Console.WriteLine($"[UnityZone:{_definition.ZoneId}:out] {args.Data}");
            }
        };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                Console.WriteLine($"[UnityZone:{_definition.ZoneId}:err] {args.Data}");
            }
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Console.WriteLine($"Zone {_definition.ZoneId} started Unity zone process from {executablePath}.");
        Console.WriteLine($"Zone {_definition.ZoneId} launch args: {startInfo.Arguments}");
        _ = Task.Run(() => WaitForPortsAsync(cancellationToken), CancellationToken.None);

        try
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public ZoneRuntimeSnapshot CreateDashboardSnapshot(string lifecycleState, DateTimeOffset lastStateChangeUtc, DateTimeOffset? idleSinceUtc)
        => new(
            _definition.ZoneId,
            _definition.Name,
            _definition.TcpPort,
            _definition.UdpPort,
            _definition.MinX,
            _definition.MaxX,
            _definition.MinZ,
            _definition.MaxZ,
            RuntimeMode,
            lifecycleState,
            lastStateChangeUtc,
            idleSinceUtc,
            0,
            _players.Count,
            0,
            0,
            _settings.AoiRadius,
            _settings.GhostMargin,
            _settings.PrewarmMargin,
            _settings.TransferInset,
            _players.Values
                .OrderBy(player => player.PlayerId)
                .Select(player => new ZonePlayerSnapshot(
                    player.SessionId,
                    player.PlayerId,
                    player.Position.X,
                    player.Position.Y,
                    player.Position.Z,
                    player.Velocity.X,
                    player.Velocity.Y,
                    player.Velocity.Z,
                    null))
                .ToArray(),
            _mobs
                .OrderBy(mob => mob.MobId, StringComparer.Ordinal)
                .Select(mob => new ZoneMobSnapshot(
                    mob.MobId,
                    mob.MobTypeId,
                    mob.Position.X,
                    mob.Position.Y,
                    mob.Position.Z,
                    mob.Velocity.X,
                    mob.Velocity.Y,
                    mob.Velocity.Z,
                    mob.State))
                .ToArray());

    public void ReportPlayerState(Guid sessionId, ulong playerId, NetworkVector3 position, NetworkVector3 velocity)
    {
        _players[sessionId] = new ExternalPlayerState(sessionId, playerId, position, velocity);
    }

    public void ReportMobStates(MobSnapshot[] mobs)
    {
        _mobs = mobs ?? Array.Empty<MobSnapshot>();
    }

    public void RemovePlayer(Guid sessionId)
    {
        _players.TryRemove(sessionId, out _);
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private async Task WaitForPortsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (_process is { HasExited: true })
                {
                    return;
                }

                using var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(_definition.Host, _definition.TcpPort, cancellationToken).ConfigureAwait(false);
                    _ready.TrySetResult(true);
                    return;
                }
                catch
                {
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            if (!_ready.Task.IsCompleted)
            {
                _ready.TrySetException(new TimeoutException($"Unity zone process for zone {_definition.ZoneId} did not open TCP {_definition.TcpPort} in time."));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EnsurePortsAvailable()
    {
        var ip = IPGlobalProperties.GetIPGlobalProperties();
        var tcpConflict = ip.GetActiveTcpListeners().Any(endpoint => endpoint.Port == _definition.TcpPort);
        var udpConflict = ip.GetActiveUdpListeners().Any(endpoint => endpoint.Port == _definition.UdpPort);

        if (!tcpConflict && !udpConflict)
        {
            return;
        }

        var conflicts = string.Join(
            ", ",
            new[]
            {
                tcpConflict ? $"TCP {_definition.TcpPort}" : null,
                udpConflict ? $"UDP {_definition.UdpPort}" : null
            }.Where(value => value != null));

        throw new InvalidOperationException(
            $"Zone {_definition.ZoneId} cannot start Unity process because {conflicts} is already in use. " +
            "Stop the existing process using that port before launching this zone.");
    }

    private sealed record ExternalPlayerState(Guid SessionId, ulong PlayerId, NetworkVector3 Position, NetworkVector3 Velocity);
}
