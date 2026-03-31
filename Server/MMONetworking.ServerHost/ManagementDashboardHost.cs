using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace MMONetworking.ServerHost;

public sealed class ManagementDashboardHost
{
    private readonly GatewayHost _gatewayHost;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ZoneSupervisor _zoneSupervisor;
    private readonly GameplayDefinitionStore _gameplayDefinitions;
    private readonly int _port;

    public ManagementDashboardHost(GatewayHost gatewayHost, SessionRegistry sessionRegistry, ZoneSupervisor zoneSupervisor, GameplayDefinitionStore gameplayDefinitions, int port)
    {
        _gatewayHost = gatewayHost;
        _sessionRegistry = sessionRegistry;
        _zoneSupervisor = zoneSupervisor;
        _gameplayDefinitions = gameplayDefinitions;
        _port = port;
    }

    public DashboardSnapshot CreateSnapshot()
        => new(
            DateTimeOffset.UtcNow,
            _gatewayHost.CreateDashboardSnapshot(),
            _sessionRegistry.CreateDashboardSnapshot(),
            _zoneSupervisor.CreateDashboardSnapshot());

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{_port}");

        var app = builder.Build();

        app.MapGet("/api/dashboard", () => Results.Json(CreateSnapshot()));
        app.MapGet("/api/gameplay-definitions", () => Results.Json(_gameplayDefinitions.GetSnapshot()));
        app.MapPut("/api/gameplay-definitions", async (HttpContext context) =>
        {
            try
            {
                var incoming = await JsonSerializer.DeserializeAsync<GameplayDefinitionsSnapshot>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    context.RequestAborted).ConfigureAwait(false);

                if (incoming is null)
                {
                    return Results.BadRequest(new { error = "Request body was empty or invalid JSON." });
                }

                var saved = _gameplayDefinitions.Update(incoming);
                return Results.Json(saved);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildHtml(), cancellationToken);
        });
        app.MapGet("/map", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(BuildMapHtml(), cancellationToken);
        });

        await app.StartAsync(cancellationToken);
        Console.WriteLine($"Management dashboard listening on http://127.0.0.1:{_port}.");
        await app.WaitForShutdownAsync(cancellationToken);
    }

    private string BuildHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>MMO Network Dashboard</title>
  <style>
    :root {
      --bg: #f4efe7;
      --panel: rgba(255,255,255,0.78);
      --panel-border: rgba(61,43,31,0.14);
      --text: #26170f;
      --muted: #6a5547;
      --accent: #a14d2d;
      --accent-soft: #f2d8b8;
      --good: #24613e;
      --warn: #a06018;
      --shadow: 0 18px 60px rgba(59,34,20,0.12);
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Georgia, "Palatino Linotype", serif;
      color: var(--text);
      background:
        radial-gradient(circle at top left, #f8e8ce 0, transparent 30%),
        radial-gradient(circle at bottom right, #e8c7ba 0, transparent 28%),
        linear-gradient(160deg, #f3ebdf 0%, #efe4d6 40%, #e7d7ca 100%);
      min-height: 100vh;
    }

    .wrap {
      width: min(1280px, calc(100vw - 32px));
      margin: 0 auto;
      padding: 28px 0 36px;
    }

    .hero {
      display: grid;
      gap: 12px;
      margin-bottom: 22px;
    }

    h1 {
      margin: 0;
      font-size: clamp(2.1rem, 5vw, 4.2rem);
      line-height: 0.95;
      letter-spacing: -0.05em;
      max-width: 10ch;
    }

    .sub {
      color: var(--muted);
      max-width: 70ch;
      font-size: 1rem;
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: 16px;
    }

    .card {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 22px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(14px);
      padding: 18px;
    }

    .stat {
      grid-column: span 3;
      min-height: 132px;
      display: grid;
      align-content: space-between;
    }

    .wide {
      grid-column: span 6;
    }

    .full {
      grid-column: 1 / -1;
    }

    .eyebrow {
      color: var(--muted);
      font-size: 0.82rem;
      text-transform: uppercase;
      letter-spacing: 0.12em;
    }

    .value {
      font-size: clamp(1.8rem, 3vw, 3rem);
      line-height: 1;
      font-weight: 700;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      padding: 6px 10px;
      border-radius: 999px;
      font-size: 0.85rem;
      background: var(--accent-soft);
      color: var(--accent);
      width: fit-content;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 14px;
      font-size: 0.95rem;
    }

    th, td {
      padding: 10px 8px;
      text-align: left;
      border-bottom: 1px solid rgba(61,43,31,0.1);
      vertical-align: top;
    }

    th {
      color: var(--muted);
      font-weight: 600;
      font-size: 0.8rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }

    .zone-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
      gap: 14px;
      margin-top: 14px;
    }

    .zone {
      background: rgba(255,255,255,0.72);
      border: 1px solid rgba(61,43,31,0.1);
      border-radius: 18px;
      padding: 14px;
      display: grid;
      gap: 10px;
    }

    .zone h3 {
      margin: 0;
      font-size: 1.2rem;
    }

    .kv {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 6px 10px;
      color: var(--muted);
      font-size: 0.94rem;
    }

    .ok { color: var(--good); }
    .warn { color: var(--warn); }
    .editor-toolbar { display: flex; gap: 10px; margin-top: 12px; flex-wrap: wrap; }
    textarea {
      width: 100%;
      min-height: 280px;
      border-radius: 12px;
      border: 1px solid rgba(61,43,31,0.2);
      background: rgba(255,255,255,0.8);
      padding: 12px;
      font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      font-size: 0.86rem;
      color: #26170f;
      resize: vertical;
    }
    button {
      border: 1px solid rgba(61,43,31,0.2);
      background: rgba(255,255,255,0.92);
      border-radius: 999px;
      padding: 8px 14px;
      color: #26170f;
      cursor: pointer;
    }

    @media (max-width: 920px) {
      .stat, .wide { grid-column: 1 / -1; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="hero">
      <div class="badge">Management Dashboard</div>
      <div class="badge"><a href="/map" style="color:inherit;text-decoration:none;">Open Map</a></div>
      <h1>Live zone and session control surface.</h1>
      <div class="sub">
        This view reflects the in-process MMO runtime. It is intended for basic operations now: gateway health, online sessions, zone populations, and transfer activity.
      </div>
    </section>

    <section class="grid">
      <article class="card stat">
        <div class="eyebrow">Sessions</div>
        <div class="value" id="sessionCount">0</div>
        <div id="lastUpdated" class="badge">Waiting for data</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Zones</div>
        <div class="value" id="zoneCount">0</div>
        <div class="badge">Realtime runtime view</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Gateway Logins</div>
        <div class="value" id="successfulLogins">0</div>
        <div class="badge">TCP 7000</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Transfers</div>
        <div class="value" id="transferCount">0</div>
        <div class="badge">Cross-zone handoffs</div>
      </article>
      <article class="card stat">
        <div class="eyebrow">Ghosts</div>
        <div class="value" id="ghostCount">0</div>
        <div class="badge">Adjacent-zone overlap</div>
      </article>

      <article class="card wide">
        <div class="eyebrow">Gateway</div>
        <table>
          <tbody>
            <tr><th>Port</th><td id="gatewayPort">-</td></tr>
            <tr><th>Connection Attempts</th><td id="gatewayAttempts">-</td></tr>
            <tr><th>Successful Logins</th><td id="gatewaySuccess">-</td></tr>
            <tr><th>Errors</th><td id="gatewayErrors">-</td></tr>
          </tbody>
        </table>
      </article>

      <article class="card wide">
        <div class="eyebrow">Zone Fleet</div>
        <div class="sub">AOI-filtered snapshots per player. Ghost replicas appear only near neighboring seams.</div>
        <div id="zones" class="zone-grid"></div>
      </article>

      <article class="card full">
        <div class="eyebrow">Sessions</div>
        <table>
          <thead>
            <tr>
              <th>Player</th>
              <th>Account</th>
              <th>Zone</th>
              <th>Position</th>
              <th>Pending Attachments</th>
            </tr>
          </thead>
          <tbody id="sessions"></tbody>
        </table>
      </article>

      <article class="card full">
        <div class="eyebrow">Gameplay Definitions</div>
        <div class="sub">Define items, skills, resources, nodes, and zone metadata from one JSON payload. Saved to <code>Documents/GameplayDefinitions.json</code>.</div>
        <textarea id="definitionsJson" spellcheck="false"></textarea>
        <div class="editor-toolbar">
          <button id="reloadDefinitions">Reload Definitions</button>
          <button id="saveDefinitions">Save Definitions</button>
          <div id="definitionStatus" class="badge">Not loaded</div>
        </div>
      </article>
    </section>
  </div>

  <script>
    const fmt = new Intl.NumberFormat();

    function render(snapshot) {
      document.getElementById('sessionCount').textContent = fmt.format(snapshot.sessions.length);
      document.getElementById('zoneCount').textContent = fmt.format(snapshot.zones.length);
      document.getElementById('successfulLogins').textContent = fmt.format(snapshot.gateway.successfulLogins);
      document.getElementById('transferCount').textContent = fmt.format(snapshot.zones.reduce((sum, zone) => sum + zone.totalTransfersInitiated, 0));
      document.getElementById('ghostCount').textContent = fmt.format(snapshot.zones.reduce((sum, zone) => sum + zone.activeGhosts, 0));
      document.getElementById('lastUpdated').textContent = `Updated ${new Date(snapshot.generatedAtUtc).toLocaleTimeString()}`;

      document.getElementById('gatewayPort').textContent = snapshot.gateway.port;
      document.getElementById('gatewayAttempts').textContent = fmt.format(snapshot.gateway.connectionAttempts);
      document.getElementById('gatewaySuccess').textContent = fmt.format(snapshot.gateway.successfulLogins);
      document.getElementById('gatewayErrors').textContent = fmt.format(snapshot.gateway.errors);

      document.getElementById('zones').innerHTML = snapshot.zones.map(zone => `
        <section class="zone">
          <div>
            <div class="badge">Zone ${zone.zoneId}</div>
            <h3>${zone.name}</h3>
          </div>
            <div class="kv">
            <div>State</div><div>${zone.lifecycleState}</div>
            <div>Mode</div><div>${zone.runtimeMode}</div>
            <div>TCP</div><div>${zone.tcpPort}</div>
            <div>UDP</div><div>${zone.udpPort}</div>
            <div>Bounds</div><div>X ${zone.minX.toFixed(0)}-${zone.maxX.toFixed(0)} / Z ${zone.minZ.toFixed(0)}-${zone.maxZ.toFixed(0)}</div>
            <div>Tick</div><div>${fmt.format(zone.tick)}</div>
            <div>Players</div><div class="${zone.activePlayers > 0 ? 'ok' : 'warn'}">${fmt.format(zone.activePlayers)}</div>
            <div>Ghosts</div><div>${fmt.format(zone.activeGhosts)}</div>
            <div>Transfers</div><div>${fmt.format(zone.totalTransfersInitiated)}</div>
            <div>AOI Radius</div><div>${zone.aoiRadius.toFixed(1)}</div>
            <div>Ghost Margin</div><div>${zone.ghostMargin.toFixed(1)}</div>
            <div>Prewarm Margin</div><div>${zone.prewarmMargin.toFixed(1)}</div>
            <div>Transfer Inset</div><div>${zone.transferInset.toFixed(1)}</div>
            <div>Last Change</div><div>${new Date(zone.lastStateChangeUtc).toLocaleTimeString()}</div>
            <div>Idle Since</div><div>${zone.idleSinceUtc ? new Date(zone.idleSinceUtc).toLocaleTimeString() : 'Active'}</div>
          </div>
        </section>
      `).join('');

      document.getElementById('sessions').innerHTML = snapshot.sessions.map(session => {
        const pending = session.pendingAttachments.length === 0
          ? '<span class="warn">None</span>'
          : session.pendingAttachments.map(p => `Zone ${p.zoneId} @ (${p.spawnX.toFixed(1)}, ${p.spawnZ.toFixed(1)})`).join('<br>');

        return `
          <tr>
            <td>${session.playerId}<br><span class="eyebrow">${session.sessionId}</span></td>
            <td>${session.accountId}</td>
            <td>${session.currentZoneId}</td>
            <td>${session.positionX.toFixed(2)}, ${session.positionY.toFixed(2)}, ${session.positionZ.toFixed(2)}</td>
            <td>${pending}</td>
          </tr>
        `;
      }).join('');
    }

    async function refresh() {
      const response = await fetch('/api/dashboard', { cache: 'no-store' });
      const data = await response.json();
      render(data);
    }

    async function loadDefinitions() {
      const response = await fetch('/api/gameplay-definitions', { cache: 'no-store' });
      const data = await response.json();
      document.getElementById('definitionsJson').value = JSON.stringify(data, null, 2);
      document.getElementById('definitionStatus').textContent = `Definitions loaded ${new Date().toLocaleTimeString()}`;
    }

    async function saveDefinitions() {
      const raw = document.getElementById('definitionsJson').value;
      try {
        const payload = JSON.parse(raw);
        const response = await fetch('/api/gameplay-definitions', {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(payload)
        });
        const result = await response.json();
        if (!response.ok) {
          throw new Error(result.error || 'Failed to save definitions');
        }
        document.getElementById('definitionsJson').value = JSON.stringify(result, null, 2);
        document.getElementById('definitionStatus').textContent = `Definitions saved ${new Date().toLocaleTimeString()}`;
      } catch (err) {
        document.getElementById('definitionStatus').textContent = `Save failed: ${err.message}`;
      }
    }

    document.getElementById('reloadDefinitions').addEventListener('click', loadDefinitions);
    document.getElementById('saveDefinitions').addEventListener('click', saveDefinitions);

    refresh();
    loadDefinitions();
    setInterval(refresh, 1500);
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildMapHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>MMO Network Map</title>
  <style>
    :root {
      --panel: rgba(255,255,255,0.82);
      --panel-border: rgba(61,43,31,0.14);
      --text: #26170f;
      --muted: #6a5547;
      --zone-fill: rgba(161,77,45,0.14);
      --zone-stroke: #a14d2d;
      --player-moving: #24613e;
      --player-idle: #a06018;
      --player-transfer: #c08b14;
      --player-ghost: #7d5ea8;
      --player-selected: #0a6c91;
      --trail: rgba(36,97,62,0.34);
      --transfer-line: rgba(160,96,24,0.72);
      --prewarm-fill: rgba(192,139,20,0.12);
      --event: rgba(10,108,145,0.84);
      --shadow: 0 18px 60px rgba(59,34,20,0.12);
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Georgia, "Palatino Linotype", serif;
      color: var(--text);
      background:
        radial-gradient(circle at top left, #f8e8ce 0, transparent 30%),
        radial-gradient(circle at bottom right, #e8c7ba 0, transparent 28%),
        linear-gradient(160deg, #f3ebdf 0%, #efe4d6 40%, #e7d7ca 100%);
      min-height: 100vh;
    }
    .wrap {
      width: min(1440px, calc(100vw - 32px));
      margin: 0 auto;
      padding: 28px 0 36px;
      display: grid;
      gap: 16px;
    }
    .card {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 22px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(14px);
      padding: 18px;
    }
    .hero {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: end;
      flex-wrap: wrap;
    }
    .title { display: grid; gap: 8px; }
    h1 {
      margin: 0;
      font-size: clamp(2rem, 4vw, 3.8rem);
      line-height: 0.95;
      letter-spacing: -0.05em;
    }
    .sub { color: var(--muted); }
    .nav a {
      display: inline-flex;
      padding: 8px 12px;
      border-radius: 999px;
      border: 1px solid var(--panel-border);
      color: var(--text);
      text-decoration: none;
      margin-left: 8px;
      background: rgba(255,255,255,0.56);
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 360px;
      gap: 16px;
    }
    .map-frame {
      position: relative;
      min-height: 720px;
      overflow: hidden;
    }
    .toolbar {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      flex-wrap: wrap;
      margin-bottom: 14px;
      color: var(--muted);
      font-size: 0.95rem;
    }
    .toolbar-group {
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
    }
    .toolbar label {
      display: inline-flex;
      gap: 8px;
      align-items: center;
    }
    #mapSvg {
      width: 100%;
      height: min(72vh, 820px);
      display: block;
      background: linear-gradient(180deg, rgba(255,255,255,0.5), rgba(255,255,255,0.2));
      border-radius: 18px;
    }
    .legend, .zone-list, .event-list, .player-detail { display: grid; gap: 10px; }
    .legend-item, .zone-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      color: var(--muted);
      font-size: 0.95rem;
    }
    .swatch {
      width: 14px;
      height: 14px;
      border-radius: 999px;
      display: inline-block;
      margin-right: 8px;
      vertical-align: middle;
    }
    .meta { color: var(--muted); font-size: 0.92rem; }
    .section-title {
      margin: 16px 0 8px;
      font-size: 0.86rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--muted);
    }
    .event-item, .detail-row {
      display: grid;
      gap: 4px;
      padding: 10px 12px;
      border-radius: 14px;
      background: rgba(255,255,255,0.54);
      border: 1px solid rgba(61,43,31,0.08);
    }
    .detail-grid {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 6px 10px;
      font-size: 0.93rem;
      color: var(--muted);
    }
    .empty { color: var(--muted); font-size: 0.92rem; padding: 8px 0; }
    @media (max-width: 1024px) {
      .layout { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card hero">
      <div class="title">
        <div class="sub">Operations Map</div>
        <h1>Zones, handoffs, and live player movement.</h1>
        <div class="sub">This view turns the runtime snapshot into a spatial debugging surface for seams, prewarm regions, and live transfers.</div>
      </div>
      <div class="nav">
        <a href="/">Dashboard</a>
        <a href="/map">Map</a>
      </div>
    </section>
    <section class="layout">
      <article class="card map-frame">
        <div class="toolbar">
          <div class="toolbar-group">
            <label><input id="toggleTrails" type="checkbox" checked> Trails</label>
            <label><input id="toggleEvents" type="checkbox" checked> Transfer events</label>
            <label><input id="toggleMargins" type="checkbox" checked> Boundary margins</label>
          </div>
          <div class="toolbar-group"><span>Refresh: 1.5s</span></div>
        </div>
        <svg id="mapSvg" viewBox="0 0 1200 720" preserveAspectRatio="xMidYMid meet"></svg>
      </article>
      <aside class="card">
        <div class="legend">
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-moving);"></span>Moving player</span><span id="movingCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-idle);"></span>Idle player</span><span id="idleCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-transfer);"></span>Transferring</span><span id="transferCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-ghost);"></span>Ghost / overlap</span><span id="ghostCount">0</span></div>
          <div class="legend-item"><span>Zones</span><span id="zoneCount">0</span></div>
          <div class="legend-item"><span>Updated</span><span id="updatedAt">-</span></div>
        </div>
        <div class="section-title">Player Detail</div>
        <div class="player-detail" id="playerDetail">
          <div class="empty">Click a player marker to inspect session, velocity, zone, and pending transfer.</div>
        </div>
        <div class="section-title">Recent Transfers</div>
        <div class="event-list" id="eventList">
          <div class="empty">No transfer events observed yet.</div>
        </div>
        <div class="section-title">Zones</div>
        <div class="zone-list" id="zoneList"></div>
      </aside>
    </section>
  </div>
  <script>
    const svg = document.getElementById('mapSvg');
    const toggleTrails = document.getElementById('toggleTrails');
    const toggleEvents = document.getElementById('toggleEvents');
    const toggleMargins = document.getElementById('toggleMargins');
    const playerDetail = document.getElementById('playerDetail');

    const trailHistory = new Map();
    const zoneHistory = new Map();
    const recentEvents = [];
    let selectedSessionId = null;
    let latestPlayers = [];

    function escapeHtml(value) {
      return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
    }

    function fmt(n) {
      return Number(n).toFixed(2);
    }

    function classifyPlayer(player) {
      const speed = Math.hypot(player.velocityX, player.velocityY, player.velocityZ);
      const hasPendingDestination = player.pendingDestinationZoneId != null;
      const isTransferring = hasPendingDestination && speed > 0.05;
      if (isTransferring) return { kind: 'transferring', color: '#c08b14', speed };
      if (hasPendingDestination) return { kind: 'ghost', color: '#7d5ea8', speed };
      if (speed > 0.05) return { kind: 'moving', color: '#24613e', speed };
      return { kind: 'idle', color: '#a06018', speed };
    }

    function pushTrail(player) {
      const key = player.sessionId;
      const existing = trailHistory.get(key) || [];
      existing.push({ x: player.positionX, z: player.positionZ, zoneId: player.zoneId, at: Date.now() });
      while (existing.length > 18) {
        existing.shift();
      }
      trailHistory.set(key, existing);
    }

    function pruneTrails(players) {
      const active = new Set(players.map(player => player.sessionId));
      for (const key of trailHistory.keys()) {
        if (!active.has(key)) {
          trailHistory.delete(key);
        }
      }
    }

    function registerZoneChanges(players) {
      const now = new Date();
      for (const player of players) {
        const previousZoneId = zoneHistory.get(player.sessionId);
        zoneHistory.set(player.sessionId, player.zoneId);
        if (previousZoneId != null && previousZoneId !== player.zoneId) {
          recentEvents.unshift({
            id: `${player.sessionId}:${now.getTime()}`,
            sessionId: player.sessionId,
            playerId: player.playerId,
            fromZoneId: previousZoneId,
            toZoneId: player.zoneId,
            x: player.positionX,
            z: player.positionZ,
            at: now.toISOString()
          });
        }
      }
      while (recentEvents.length > 12) {
        recentEvents.pop();
      }
    }

    function renderPlayerDetail() {
      if (!selectedSessionId) {
        playerDetail.innerHTML = '<div class="empty">Click a player marker to inspect session, velocity, zone, and pending transfer.</div>';
        return;
      }
      const player = latestPlayers.find(candidate => candidate.sessionId === selectedSessionId);
      if (!player) {
        playerDetail.innerHTML = '<div class="empty">Selected player is no longer present in the current snapshot.</div>';
        return;
      }
      const state = classifyPlayer(player);
      playerDetail.innerHTML = `
        <div class="detail-row">
          <div><strong>P${player.playerId}</strong> <span class="meta">${escapeHtml(state.kind)}</span></div>
          <div class="detail-grid">
            <span>Session</span><span>${escapeHtml(player.sessionId)}</span>
            <span>Zone</span><span>${player.zoneId}</span>
            <span>Position</span><span>${fmt(player.positionX)}, ${fmt(player.positionZ)}</span>
            <span>Velocity</span><span>${fmt(player.velocityX)}, ${fmt(player.velocityZ)}</span>
            <span>Pending dest</span><span>${player.pendingDestinationZoneId ?? '-'}</span>
          </div>
        </div>`;
    }

    function renderEvents() {
      const list = document.getElementById('eventList');
      if (recentEvents.length === 0) {
        list.innerHTML = '<div class="empty">No transfer events observed yet.</div>';
        return;
      }
      list.innerHTML = recentEvents.map(eventItem => `
        <div class="event-item">
          <div><strong>P${eventItem.playerId}</strong> moved ${eventItem.fromZoneId} → ${eventItem.toZoneId}</div>
          <div class="meta">${new Date(eventItem.at).toLocaleTimeString()} at ${fmt(eventItem.x)}, ${fmt(eventItem.z)}</div>
        </div>`).join('');
    }

    function render(snapshot) {
      const zones = snapshot.zones;
      if (zones.length === 0) {
        svg.innerHTML = '';
        return;
      }

      const world = zones.reduce((acc, zone) => ({
        minX: Math.min(acc.minX, zone.minX),
        maxX: Math.max(acc.maxX, zone.maxX),
        minZ: Math.min(acc.minZ, zone.minZ),
        maxZ: Math.max(acc.maxZ, zone.maxZ)
      }), { minX: Number.POSITIVE_INFINITY, maxX: Number.NEGATIVE_INFINITY, minZ: Number.POSITIVE_INFINITY, maxZ: Number.NEGATIVE_INFINITY });

      const pad = 36;
      const width = 1200;
      const height = 720;
      const innerWidth = width - pad * 2;
      const innerHeight = height - pad * 2;
      const worldWidth = Math.max(1, world.maxX - world.minX);
      const worldHeight = Math.max(1, world.maxZ - world.minZ);
      const scale = Math.min(innerWidth / worldWidth, innerHeight / worldHeight);
      const offsetX = pad + (innerWidth - worldWidth * scale) / 2;
      const offsetY = pad + (innerHeight - worldHeight * scale) / 2;
      const projectX = x => offsetX + (x - world.minX) * scale;
      const projectY = z => height - (offsetY + (z - world.minZ) * scale);

      let movingCount = 0;
      let idleCount = 0;
      let transferCount = 0;
      let ghostCount = 0;

      const flattenedPlayers = zones.flatMap(zone => zone.players.map(player => ({
        ...player,
        zoneId: zone.zoneId,
        zoneName: zone.name
      })));

      latestPlayers = flattenedPlayers;
      registerZoneChanges(flattenedPlayers);
      flattenedPlayers.forEach(pushTrail);
      pruneTrails(flattenedPlayers);

      const zoneRects = zones.map(zone => {
        const x = projectX(zone.minX);
        const y = projectY(zone.maxZ);
        const w = (zone.maxX - zone.minX) * scale;
        const h = (zone.maxZ - zone.minZ) * scale;
        const rightBoundaryX = projectX(zone.maxX);
        const prewarmStartX = zone.prewarmMargin > 0 ? projectX(Math.max(zone.minX, zone.maxX - zone.prewarmMargin)) : x;
        const prewarmWidth = zone.prewarmMargin > 0 ? Math.max(0, rightBoundaryX - prewarmStartX) : 0;
        return `
          <g>
            <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" fill="var(--zone-fill)" stroke="var(--zone-stroke)" stroke-width="3"></rect>
            ${toggleMargins.checked && prewarmWidth > 0 ? `<rect x="${prewarmStartX}" y="${y}" width="${prewarmWidth}" height="${h}" fill="var(--prewarm-fill)"></rect>` : ''}
            ${toggleMargins.checked ? `<line x1="${rightBoundaryX}" y1="${y}" x2="${rightBoundaryX}" y2="${y + h}" stroke="var(--transfer-line)" stroke-width="2.5" stroke-dasharray="8 8"></line>` : ''}
            <text x="${x + 16}" y="${y + 28}" font-size="20" fill="#26170f" font-weight="700">${escapeHtml(zone.name)} (${zone.zoneId})</text>
            <text x="${x + 16}" y="${y + 52}" font-size="15" fill="#6a5547">${escapeHtml(zone.lifecycleState)} · ${escapeHtml(zone.runtimeMode)}</text>
          </g>`;
      }).join('');

      const trails = toggleTrails.checked
        ? flattenedPlayers.map(player => {
            const points = trailHistory.get(player.sessionId) || [];
            if (points.length < 2) return '';
            const path = points.map(point => `${projectX(point.x)},${projectY(point.z)}`).join(' ');
            return `<polyline points="${path}" fill="none" stroke="var(--trail)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"></polyline>`;
          }).join('')
        : '';

      const eventMarkers = toggleEvents.checked
        ? recentEvents.map(eventItem => `
            <g>
              <circle cx="${projectX(eventItem.x)}" cy="${projectY(eventItem.z)}" r="8" fill="none" stroke="var(--event)" stroke-width="3"></circle>
              <circle cx="${projectX(eventItem.x)}" cy="${projectY(eventItem.z)}" r="3" fill="var(--event)"></circle>
            </g>`).join('')
        : '';

      const playerDots = flattenedPlayers.map(player => {
        const state = classifyPlayer(player);
        if (state.kind === 'moving') movingCount++;
        if (state.kind === 'idle') idleCount++;
        if (state.kind === 'transferring') transferCount++;
        if (state.kind === 'ghost') ghostCount++;
        const x = projectX(player.positionX);
        const y = projectY(player.positionZ);
        const selected = selectedSessionId === player.sessionId;
        const headingScale = Math.max(12, Math.min(28, state.speed * 24));
        const divisor = Math.max(state.speed, 0.001);
        const dx = state.speed > 0.01 ? (player.velocityX / divisor) * headingScale : 0;
        const dy = state.speed > 0.01 ? (-player.velocityZ / divisor) * headingScale : 0;
        return `
          <g data-session-id="${player.sessionId}" style="cursor:pointer">
            <circle cx="${x}" cy="${y}" r="${selected ? 13 : 10}" fill="${state.color}" stroke="${selected ? '#0a6c91' : '#fffaf1'}" stroke-width="${selected ? 4 : 3}"></circle>
            ${state.speed > 0.05 ? `<line x1="${x}" y1="${y}" x2="${x + dx}" y2="${y + dy}" stroke="${state.color}" stroke-width="3" stroke-linecap="round"></line>` : ''}
            <text x="${x + 12}" y="${y - 12}" font-size="14" fill="#26170f">P${player.playerId}</text>
          </g>`;
      }).join('');

      const axes = `
        <g opacity="0.55">
          <text x="${pad}" y="${height - 10}" font-size="14" fill="#6a5547">X: ${world.minX.toFixed(0)} - ${world.maxX.toFixed(0)}</text>
          <text x="${width - 180}" y="${height - 10}" font-size="14" fill="#6a5547">Z: ${world.minZ.toFixed(0)} - ${world.maxZ.toFixed(0)}</text>
        </g>`;

      svg.innerHTML = `${zoneRects}${trails}${eventMarkers}${playerDots}${axes}`;

      document.getElementById('movingCount').textContent = movingCount;
      document.getElementById('idleCount').textContent = idleCount;
      document.getElementById('transferCount').textContent = transferCount;
      document.getElementById('ghostCount').textContent = ghostCount;
      document.getElementById('zoneCount').textContent = zones.length;
      document.getElementById('updatedAt').textContent = new Date(snapshot.generatedAtUtc).toLocaleTimeString();
      document.getElementById('zoneList').innerHTML = zones.map(zone => `
        <div class="zone-item">
          <span>Zone ${zone.zoneId} ${escapeHtml(zone.name)}</span>
          <span class="meta">${zone.players.length} players / ${zone.activeGhosts} ghosts</span>
        </div>
        <div class="meta">Bounds X ${zone.minX.toFixed(0)}-${zone.maxX.toFixed(0)} / Z ${zone.minZ.toFixed(0)}-${zone.maxZ.toFixed(0)}</div>
        <div class="meta">AOI ${zone.aoiRadius.toFixed(0)} / ghost margin ${zone.ghostMargin.toFixed(0)} / prewarm ${zone.prewarmMargin.toFixed(0)} / inset ${zone.transferInset.toFixed(0)}</div>`
      ).join('');

      svg.querySelectorAll('[data-session-id]').forEach(node => {
        node.addEventListener('click', event => {
          event.stopPropagation();
          selectedSessionId = node.getAttribute('data-session-id');
          renderPlayerDetail();
          render(snapshot);
        }, { once: true });
      });

      renderPlayerDetail();
      renderEvents();
    }

    toggleTrails.addEventListener('change', refresh);
    toggleEvents.addEventListener('change', refresh);
    toggleMargins.addEventListener('change', refresh);

    svg.addEventListener('click', event => {
      if (event.target === svg) {
        selectedSessionId = null;
        renderPlayerDetail();
      }
    });

    async function refresh() {
      try {
        const response = await fetch('/api/dashboard', { cache: 'no-store' });
        const data = await response.json();
        render(data);
      } catch (error) {
        console.error(error);
      }
    }

    refresh();
    setInterval(refresh, 1500);
  </script>
</body>
</html>
""";

        return html;
    }

    private string BuildMapHtmlLegacy()
    {
        var html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>MMO Network Map</title>
  <style>
    :root {
      --bg: #efe7da;
      --panel: rgba(255,255,255,0.82);
      --panel-border: rgba(61,43,31,0.14);
      --text: #26170f;
      --muted: #6a5547;
      --zone-fill: rgba(161,77,45,0.14);
      --zone-stroke: #a14d2d;
      --player: #24613e;
      --player-stopped: #a06018;
      --shadow: 0 18px 60px rgba(59,34,20,0.12);
    }

    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Georgia, "Palatino Linotype", serif;
      color: var(--text);
      background:
        radial-gradient(circle at top left, #f8e8ce 0, transparent 30%),
        radial-gradient(circle at bottom right, #e8c7ba 0, transparent 28%),
        linear-gradient(160deg, #f3ebdf 0%, #efe4d6 40%, #e7d7ca 100%);
      min-height: 100vh;
    }

    .wrap {
      width: min(1380px, calc(100vw - 32px));
      margin: 0 auto;
      padding: 28px 0 36px;
      display: grid;
      gap: 16px;
    }

    .card {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 22px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(14px);
      padding: 18px;
    }

    .hero {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: end;
      flex-wrap: wrap;
    }

    .title {
      display: grid;
      gap: 8px;
    }

    h1 {
      margin: 0;
      font-size: clamp(2rem, 4vw, 3.6rem);
      line-height: 0.95;
      letter-spacing: -0.05em;
    }

    .sub { color: var(--muted); }
    .nav a {
      display: inline-flex;
      padding: 8px 12px;
      border-radius: 999px;
      border: 1px solid var(--panel-border);
      color: var(--text);
      text-decoration: none;
      margin-left: 8px;
      background: rgba(255,255,255,0.56);
    }

    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 320px;
      gap: 16px;
    }

    .map-frame {
      position: relative;
      min-height: 720px;
      overflow: hidden;
    }

    #mapSvg {
      width: 100%;
      height: min(72vh, 820px);
      display: block;
      background: linear-gradient(180deg, rgba(255,255,255,0.5), rgba(255,255,255,0.2));
      border-radius: 18px;
    }

    .legend, .zone-list {
      display: grid;
      gap: 10px;
    }

    .legend-item, .zone-item {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      color: var(--muted);
      font-size: 0.95rem;
    }

    .swatch {
      width: 14px;
      height: 14px;
      border-radius: 999px;
      display: inline-block;
      margin-right: 8px;
      vertical-align: middle;
    }

    .meta {
      color: var(--muted);
      font-size: 0.92rem;
    }

    @media (max-width: 1024px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <section class="card hero">
      <div class="title">
        <div class="sub">Operations Map</div>
        <h1>Zones, bounds, and live player positions.</h1>
        <div class="sub">This view projects the current dashboard snapshot into a 2D world map.</div>
      </div>
      <div class="nav">
        <a href="/">Dashboard</a>
        <a href="/map">Map</a>
      </div>
    </section>

    <section class="layout">
      <article class="card map-frame">
        <svg id="mapSvg" viewBox="0 0 1200 720" preserveAspectRatio="xMidYMid meet"></svg>
      </article>

      <aside class="card">
        <div class="legend">
          <div class="legend-item"><span><span class="swatch" style="background: var(--player);"></span>Moving player</span><span id="movingCount">0</span></div>
          <div class="legend-item"><span><span class="swatch" style="background: var(--player-stopped);"></span>Idle player</span><span id="idleCount">0</span></div>
          <div class="legend-item"><span>Zones</span><span id="zoneCount">0</span></div>
          <div class="legend-item"><span>Updated</span><span id="updatedAt">-</span></div>
        </div>
        <hr style="border:none;border-top:1px solid rgba(61,43,31,0.12);margin:16px 0;">
        <div class="zone-list" id="zoneList"></div>
      </aside>
    </section>
  </div>

  <script>
    const svg = document.getElementById('mapSvg');

    function escapeHtml(value) {
      return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
    }

    function render(snapshot) {
      const zones = snapshot.zones;
      if (zones.length === 0) {
        svg.innerHTML = '';
        return;
      }

      const world = zones.reduce((acc, zone) => ({
        minX: Math.min(acc.minX, zone.minX),
        maxX: Math.max(acc.maxX, zone.maxX),
        minZ: Math.min(acc.minZ, zone.minZ),
        maxZ: Math.max(acc.maxZ, zone.maxZ)
      }), { minX: Number.POSITIVE_INFINITY, maxX: Number.NEGATIVE_INFINITY, minZ: Number.POSITIVE_INFINITY, maxZ: Number.NEGATIVE_INFINITY });

      const pad = 36;
      const width = 1200;
      const height = 720;
      const innerWidth = width - pad * 2;
      const innerHeight = height - pad * 2;
      const worldWidth = Math.max(1, world.maxX - world.minX);
      const worldHeight = Math.max(1, world.maxZ - world.minZ);
      const scale = Math.min(innerWidth / worldWidth, innerHeight / worldHeight);
      const offsetX = pad + (innerWidth - worldWidth * scale) / 2;
      const offsetY = pad + (innerHeight - worldHeight * scale) / 2;
      const projectX = x => offsetX + (x - world.minX) * scale;
      const projectY = z => height - (offsetY + (z - world.minZ) * scale);

      let movingCount = 0;
      let idleCount = 0;

      const zoneRects = zones.map(zone => {
        const x = projectX(zone.minX);
        const y = projectY(zone.maxZ);
        const w = (zone.maxX - zone.minX) * scale;
        const h = (zone.maxZ - zone.minZ) * scale;
        return `
          <g>
            <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="18" fill="var(--zone-fill)" stroke="var(--zone-stroke)" stroke-width="3"></rect>
            <text x="${x + 16}" y="${y + 28}" font-size="20" fill="#26170f" font-weight="700">${escapeHtml(zone.name)} (${zone.zoneId})</text>
            <text x="${x + 16}" y="${y + 52}" font-size="15" fill="#6a5547">${escapeHtml(zone.lifecycleState)} · ${escapeHtml(zone.runtimeMode)}</text>
          </g>
        `;
      }).join('');

      const playerDots = zones.flatMap(zone => zone.players.map(player => {
        const speed = Math.abs(player.velocityX) + Math.abs(player.velocityY) + Math.abs(player.velocityZ);
        const moving = speed > 0.05;
        if (moving) movingCount++; else idleCount++;
        const x = projectX(player.positionX);
        const y = projectY(player.positionZ);
        const color = moving ? '#24613e' : '#a06018';
        return `
          <g>
            <circle cx="${x}" cy="${y}" r="9" fill="${color}" stroke="#fffaf1" stroke-width="3"></circle>
            <text x="${x + 12}" y="${y - 12}" font-size="14" fill="#26170f">P${player.playerId}</text>
          </g>
        `;
      })).join('');

      const axes = `
        <g opacity="0.55">
          <text x="${pad}" y="${height - 10}" font-size="14" fill="#6a5547">X: ${world.minX.toFixed(0)} - ${world.maxX.toFixed(0)}</text>
          <text x="${width - 180}" y="${height - 10}" font-size="14" fill="#6a5547">Z: ${world.minZ.toFixed(0)} - ${world.maxZ.toFixed(0)}</text>
        </g>
      `;

      svg.innerHTML = `${zoneRects}${playerDots}${axes}`;

      document.getElementById('movingCount').textContent = movingCount;
      document.getElementById('idleCount').textContent = idleCount;
      document.getElementById('zoneCount').textContent = zones.length;
      document.getElementById('updatedAt').textContent = new Date(snapshot.generatedAtUtc).toLocaleTimeString();
      document.getElementById('zoneList').innerHTML = zones.map(zone => `
        <div class="zone-item">
          <span>Zone ${zone.zoneId} ${escapeHtml(zone.name)}</span>
          <span class="meta">${zone.players.length} players</span>
        </div>
        <div class="meta">Bounds X ${zone.minX.toFixed(0)}-${zone.maxX.toFixed(0)} / Z ${zone.minZ.toFixed(0)}-${zone.maxZ.toFixed(0)}</div>
      `).join('');
    }

    async function refresh() {
      const response = await fetch('/api/dashboard', { cache: 'no-store' });
      const data = await response.json();
      render(data);
    }

    refresh();
    setInterval(refresh, 1500);
  </script>
</body>
</html>
""";

        return html;
    }
}
