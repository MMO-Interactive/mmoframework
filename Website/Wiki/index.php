<?php

declare(strict_types=1);

require_once __DIR__ . '/api_client.php';

$definitions = fetch_api_json('/api/gameplay-definitions');
$dashboard = fetch_api_json('/api/dashboard');
$crafting = fetch_api_json('/api/crafting/recipes');
$analytics = fetch_api_json('/api/analytics/overview');

$defData = is_array($definitions['data'] ?? null) ? $definitions['data'] : [];
$dashData = is_array($dashboard['data'] ?? null) ? $dashboard['data'] : [];
$craftData = is_array($crafting['data'] ?? null) ? $crafting['data'] : [];
$analyticsData = is_array($analytics['data'] ?? null) ? $analytics['data'] : [];

$items = is_array($defData['items'] ?? null) ? $defData['items'] : [];
$skills = is_array($defData['skills'] ?? null) ? $defData['skills'] : [];
$resources = is_array($defData['resources'] ?? null) ? $defData['resources'] : [];
$nodes = is_array($defData['nodes'] ?? null) ? $defData['nodes'] : [];
$zones = is_array($defData['zones'] ?? null) ? $defData['zones'] : [];
$sessions = is_array($dashData['sessions'] ?? null) ? $dashData['sessions'] : [];
$zoneRuntime = is_array($dashData['zones'] ?? null) ? $dashData['zones'] : [];
$recipes = is_array($craftData['recipes'] ?? null) ? $craftData['recipes'] : [];

$runtimeByZoneId = [];
foreach ($zoneRuntime as $runtime) {
    $runtimeByZoneId[safe_int($runtime['zoneId'] ?? 0)] = $runtime;
}

$zonesWithRuntime = [];
foreach ($zones as $zone) {
    $zoneId = safe_int($zone['zoneId'] ?? 0);
    $runtime = $runtimeByZoneId[$zoneId] ?? [];
    $zonesWithRuntime[] = [
        'zoneId' => $zoneId,
        'name' => safe_text($zone['name'] ?? 'Unknown'),
        'minX' => safe_text($zone['minX'] ?? '0'),
        'maxX' => safe_text($zone['maxX'] ?? '0'),
        'minZ' => safe_text($zone['minZ'] ?? '0'),
        'maxZ' => safe_text($zone['maxZ'] ?? '0'),
        'players' => safe_int($runtime['activePlayers'] ?? 0),
        'ghosts' => safe_int($runtime['activeGhosts'] ?? 0),
        'mobs' => is_array($runtime['mobs'] ?? null) ? count($runtime['mobs']) : 0,
        'lifecycle' => safe_text($runtime['lifecycleState'] ?? 'Dormant'),
        'runtimeMode' => safe_text($runtime['runtimeMode'] ?? 'Defined'),
    ];
}

usort($zonesWithRuntime, static function (array $left, array $right): int {
    return [$right['players'], $left['zoneId']] <=> [$left['players'], $right['zoneId']];
});

$featuredZones = array_slice($zonesWithRuntime, 0, 3);
$allSkills = $skills;
usort($allSkills, static function (array $left, array $right): int {
    return strcasecmp(safe_text($left['name'] ?? ''), safe_text($right['name'] ?? ''));
});
$featuredRecipes = array_slice($recipes, 0, 6);

$heroMetrics = [
    ['value' => count($zones), 'label' => 'Sharded regions'],
    ['value' => count($skills), 'label' => 'Trainable skills'],
    ['value' => count($nodes), 'label' => 'Harvest nodes'],
    ['value' => count($sessions), 'label' => 'Live adventurers'],
];

$operationsMetrics = [
    ['label' => 'Gateway logins', 'value' => safe_int($dashData['gateway']['successfulLogins'] ?? 0)],
    ['label' => 'Active sessions', 'value' => count($sessions)],
    ['label' => 'Crafting recipes', 'value' => count($recipes)],
    ['label' => 'Resource families', 'value' => count($resources)],
];

$analyticsMetrics = [
    ['label' => 'Total accounts', 'value' => safe_int($analyticsData['accounts']['totalAccounts'] ?? 0)],
    ['label' => 'Online accounts', 'value' => safe_int($analyticsData['accounts']['onlineAccounts'] ?? 0)],
    ['label' => 'Online players', 'value' => safe_int($analyticsData['sessions']['onlinePlayers'] ?? 0)],
    ['label' => 'Combat actions (24h)', 'value' => safe_int($analyticsData['combat']['actions24h'] ?? 0)],
];

function render_status_badge(array $result): string
{
    $ok = (bool)($result['ok'] ?? false);
    $class = $ok ? 'status-ok' : 'status-error';
    $label = $ok ? 'Live' : 'Degraded';
    return '<span class="status-pill ' . $class . '">' . $label . '</span>';
}

function render_api_card(string $title, array $result): string
{
    $ok = (bool)($result['ok'] ?? false);
    $status = safe_int($result['status'] ?? 0);
    $detail = $ok
        ? 'HTTP ' . $status
        : safe_text($result['error'] ?? 'Unavailable');

    return sprintf(
        '<article class="api-card"><div><strong>%s</strong>%s</div><div class="api-detail">%s</div></article>',
        htmlspecialchars($title),
        render_status_badge($result),
        htmlspecialchars($detail)
    );
}

?>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Rise of Heroes</title>
  <link rel="stylesheet" href="styles.css" />
</head>
<body>
  <div class="page-shell">
    <header class="masthead">
      <div class="top-strip">
        <div class="crest">Rise of Heroes</div>
        <nav class="top-nav">
          <a href="#world">World</a>
          <a href="#skills">Skills</a>
          <a href="#crafting">Crafting</a>
          <a href="#operations">Operations</a>
          <a href="/map">Live Map</a>
        </nav>
      </div>

      <div class="hero-ribbon">
        <div class="hero-ribbon-left">Persistent sandbox MMO</div>
        <div class="hero-ribbon-center">RISE OF HEROES</div>
        <div class="hero-ribbon-right"><?= render_status_badge($dashboard) ?></div>
      </div>

      <section class="hero">
        <div class="hero-copy">
          <div class="eyebrow">Forge your name into a living world</div>
          <h1>A skill-based sandbox MMORPG where empires, trade routes, and frontier wars are built by players.</h1>
          <p>
            Rise of Heroes is built around long-term progression, territorial expansion, gathering, crafting,
            and a world simulation that keeps moving while the realm is live.
          </p>
          <div class="hero-actions">
            <a class="btn btn-primary" href="#world">Enter the frontier</a>
            <a class="btn btn-secondary" href="/map">Watch live world traffic</a>
          </div>
          <div class="metric-row">
            <?php foreach ($heroMetrics as $metric): ?>
              <div class="metric-pill">
                <strong><?= safe_int($metric['value']) ?></strong>
                <span><?= htmlspecialchars($metric['label']) ?></span>
              </div>
            <?php endforeach; ?>
          </div>
        </div>

        <aside class="hero-panel">
          <div class="panel-title">Realm pulse</div>
          <div class="hero-panel-copy">
            Online accounts, active sessions, and authored world content are pulled directly from the running control plane.
          </div>
          <div class="stacked-metrics">
            <?php foreach ($operationsMetrics as $metric): ?>
              <div class="stacked-metric">
                <span><?= htmlspecialchars($metric['label']) ?></span>
                <strong><?= safe_int($metric['value']) ?></strong>
              </div>
            <?php endforeach; ?>
          </div>
        </aside>
      </section>
    </header>

    <main class="content">
      <section class="section-grid" id="world">
        <article class="panel feature-panel">
          <div class="eyebrow">The promise</div>
          <h2>Shape a frontier instead of riding a theme park.</h2>
          <div class="feature-list">
            <div class="feature-item">
              <strong>Persistent territory</strong>
              <span>Zones, resources, mobs, and online activity all exist inside a coherent world coordinate space.</span>
            </div>
            <div class="feature-item">
              <strong>Player-driven economy</strong>
              <span>Gather, refine, craft, transport, and build long-tail value through production chains.</span>
            </div>
            <div class="feature-item">
              <strong>Skill over class</strong>
              <span>Characters advance through use, specialization, and sustained effort across dozens of learnable disciplines.</span>
            </div>
            <div class="feature-item">
              <strong>Live operations foundation</strong>
              <span>The same dashboard stack that powers the server can expose world health, topology, assets, and content data.</span>
            </div>
          </div>
        </article>

        <article class="panel world-panel">
          <div class="eyebrow">Region overview</div>
          <h2>Configured world regions</h2>
          <div class="zone-cards">
            <?php foreach ($featuredZones as $zone): ?>
              <div class="zone-card">
                <div class="zone-card-head">
                  <strong><?= htmlspecialchars($zone['name']) ?></strong>
                  <span>Zone <?= safe_int($zone['zoneId']) ?></span>
                </div>
                <div class="zone-card-meta">X <?= htmlspecialchars($zone['minX']) ?>-<?= htmlspecialchars($zone['maxX']) ?> / Z <?= htmlspecialchars($zone['minZ']) ?>-<?= htmlspecialchars($zone['maxZ']) ?></div>
                <div class="zone-card-meta"><?= htmlspecialchars($zone['lifecycle']) ?> · <?= htmlspecialchars($zone['runtimeMode']) ?></div>
                <div class="zone-card-stats">
                  <span><?= safe_int($zone['players']) ?> players</span>
                  <span><?= safe_int($zone['mobs']) ?> mobs</span>
                  <span><?= safe_int($zone['ghosts']) ?> ghosts</span>
                </div>
              </div>
            <?php endforeach; ?>
          </div>
        </article>
      </section>

      <section class="panel" id="skills">
        <div class="section-head">
          <div>
            <div class="eyebrow">Skill-based progression</div>
            <h2>Master trades, combat forms, and frontier survival.</h2>
          </div>
          <p>
            No rigid class picks. Rise of Heroes is built around broad skill growth and specialization paths that let one
            character evolve through labor, warfare, logistics, and craftsmanship. The live skill catalog currently defines
            <strong><?= count($allSkills) ?></strong> trainable disciplines.
          </p>
        </div>
        <div class="skill-grid">
          <?php foreach ($allSkills as $skill): ?>
            <div class="skill-tile">
              <strong><?= htmlspecialchars(safe_text($skill['name'] ?? 'Unknown')) ?></strong>
              <span><?= htmlspecialchars(str_replace('_', ' ', safe_text($skill['id'] ?? ''))) ?></span>
              <span>Cap <?= safe_int($skill['maxValue'] ?? 0) ?></span>
            </div>
          <?php endforeach; ?>
        </div>
      </section>

      <section class="section-grid" id="crafting">
        <article class="panel">
          <div class="eyebrow">Resource game</div>
          <h2>Gathering starts in the world.</h2>
          <p class="section-copy">
            Resource definitions and node placements are live-authored. Harvestable nodes, active resource families,
            and crafting outputs all flow from the same control plane.
          </p>
          <div class="resource-strip">
            <div class="resource-stat"><strong><?= count($resources) ?></strong><span>Resource families</span></div>
            <div class="resource-stat"><strong><?= count($nodes) ?></strong><span>Placed nodes</span></div>
            <div class="resource-stat"><strong><?= count($items) ?></strong><span>Item definitions</span></div>
          </div>
        </article>

        <article class="panel">
          <div class="eyebrow">Crafting examples</div>
          <h2>First-pass production recipes</h2>
          <div class="recipe-list">
            <?php foreach ($featuredRecipes as $recipe): ?>
              <?php
                $ingredients = is_array($recipe['ingredients'] ?? null) ? $recipe['ingredients'] : [];
                $parts = [];
                foreach ($ingredients as $ingredient) {
                    $parts[] = safe_int($ingredient['quantity'] ?? 0) . 'x ' . safe_text($ingredient['itemId'] ?? '');
                }
              ?>
              <div class="recipe-item">
                <div class="recipe-title">
                  <strong><?= htmlspecialchars(safe_text($recipe['name'] ?? 'Unknown Recipe')) ?></strong>
                  <span><?= safe_int($recipe['craftSeconds'] ?? 0) ?>s</span>
                </div>
                <div class="recipe-output"><?= safe_int($recipe['outputQuantity'] ?? 0) ?>x <?= htmlspecialchars(safe_text($recipe['outputItemId'] ?? '')) ?></div>
                <div class="recipe-ingredients"><?= htmlspecialchars(implode(' • ', $parts)) ?></div>
              </div>
            <?php endforeach; ?>
          </div>
        </article>
      </section>

      <section class="panel" id="operations">
        <div class="section-head">
          <div>
            <div class="eyebrow">Live world operations</div>
            <h2>Built to be observed while it runs.</h2>
          </div>
          <p>
            This website is driven from the same dashboard APIs used by the control plane. It can surface authored content,
            live traffic, and live-ops health without leaving the running world.
          </p>
        </div>

        <div class="ops-grid">
          <div class="ops-column">
            <h3>Realm status</h3>
            <div class="mini-metrics">
              <?php foreach ($analyticsMetrics as $metric): ?>
                <div class="mini-metric">
                  <span><?= htmlspecialchars($metric['label']) ?></span>
                  <strong><?= safe_int($metric['value']) ?></strong>
                </div>
              <?php endforeach; ?>
            </div>
          </div>

          <div class="ops-column">
            <h3>API health</h3>
            <div class="api-grid">
              <?= render_api_card('Gameplay definitions', $definitions) ?>
              <?= render_api_card('Dashboard snapshot', $dashboard) ?>
              <?= render_api_card('Crafting recipes', $crafting) ?>
              <?= render_api_card('Analytics overview', $analytics) ?>
            </div>
          </div>
        </div>
      </section>

      <section class="cta-banner">
        <div>
          <div class="eyebrow">Rise with the realm</div>
          <h2>Stand up cities, control trade, and leave a permanent mark on the frontier.</h2>
        </div>
        <div class="cta-actions">
          <a class="btn btn-primary" href="/map">Open operations map</a>
          <a class="btn btn-secondary" href="<?= htmlspecialchars(api_base_url()) ?>">Open control plane</a>
        </div>
      </section>
    </main>
  </div>
</body>
</html>
