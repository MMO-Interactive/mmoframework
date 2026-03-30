# MMO Networking Foundation

## Transport split

- TCP carries low-rate, ordered control traffic: gateway login, zone attach, transfer preparation, transfer commit, heartbeats, and error messages.
- UDP carries high-rate realtime traffic: client movement input, server world snapshots, and the lightweight probe that binds a client's current UDP socket to the active zone.
- Shared contracts live in `Shared/MMONetworking` so the Unity client and the server host use the same wire format and message definitions.

## Current topology

- `GatewayHost` listens on TCP `7000`.
- `ManagementDashboardHost` listens on HTTP `7080`.
- `ZoneHost` instances are lifecycle-managed and only bind their TCP/UDP ports when started on demand.
- `Program.cs` boots two example zones:
  - Zone 1: `TCP 7101`, `UDP 7201`, world bounds `X 0-100`, `Z 0-100`
  - Zone 2: `TCP 7102`, `UDP 7202`, world bounds `X 100-200`, `Z 0-100`
- `SessionRegistry` is the authority for session ids, player ids, and temporary transfer tokens.
- The management dashboard exposes `/api/dashboard` plus a basic browser UI at `/` for sessions, zones, gateway counters, and transfer activity.
- `ZoneSupervisor` starts a zone when a login or transfer targets it, and stops it after two minutes with zero active players.

## Seamless zone transition flow

1. The Unity client connects to the gateway over TCP and sends `ClientHello`.
2. The gateway creates a session, chooses the initial zone, issues a transfer token for that zone, and returns `HelloAccepted`.
   Before issuing the response, the gateway tells `ZoneSupervisor` to start that zone if it is currently cold.
3. The client opens the zone TCP connection and sends `AttachToZone`.
4. The zone validates the transfer token, attaches the player, and returns `AttachAccepted`.
5. The client opens UDP to the same zone and sends `TransferProbe`.
6. The zone binds the client's UDP endpoint and starts sending `WorldSnapshot` datagrams while consuming `ClientInput`.
7. When the player's position crosses the zone bounds, the current zone issues `ZoneTransferPrepare` over TCP with the next zone's host/ports, a new transfer token, and the spawn position inside the destination zone.
   Before doing that, the current zone asks `ZoneSupervisor` to ensure the destination zone is running.
8. The client opens the new zone TCP and UDP connections immediately, attaches with the provided token, and starts consuming snapshots from the destination zone.
9. The destination zone returns `ZoneTransferCommitted`, and the old zone removes the stale player after the session registry shows the new active zone.

## Why this scales

- The gateway is only responsible for login and first routing, so it does not sit on the hot path for snapshots.
- Zone ownership is explicit. Each zone simulates and snapshots only the players currently inside its bounds.
- Cold zones do not consume bound ports or simulation time. They wake only when routed traffic needs them.
- Zone transfer tokens decouple handoff authorization from long-lived sockets. That lets you move players between processes or machines without relying on shared socket state.
- UDP traffic stays local to the active zone server. TCP remains the reliable control plane for migration and recovery.

## What to change for production scale

- Replace the in-process `SessionRegistry` with a distributed session/transfer service backed by Redis or a durable coordination layer.
- Run each `ZoneHost` in its own process or container and register it dynamically instead of hardcoding zones in `Program.cs`.
- Add interest management so snapshots are filtered by AOI rather than broadcasting the whole zone roster to every player.
- Add reliability and sequence windows on selected UDP messages if you need server-authoritative skill casts, projectiles, or state deltas that cannot simply be refreshed next tick.
- Add authentication and signed transfer tokens. The sample currently trusts the gateway and zone fabric inside one trusted environment.
- Add scene or shard warmup so the destination zone can stream nearby entity state before the player actually crosses the seam.
- If Unity zone servers become separate processes, move `ZoneSupervisor` from in-process host management to actual process orchestration and health checks.

## Files

- Server host: `Server/MMONetworking.ServerHost`
- Shared contracts: `Shared/MMONetworking`
- Unity sample client: `Client/UnitySample/Assets/Scripts/MMONetworking/MmoNetworkClient.cs`

## Run

```powershell
dotnet build Server\MMONetworking.ServerHost\MMONetworking.ServerHost.csproj
dotnet run --project Server\MMONetworking.ServerHost\MMONetworking.ServerHost.csproj
```

Open `http://127.0.0.1:7080/` for the dashboard.

Use the Unity sample component as the client entry point, or port the same logic into your existing network bootstrap objects.
