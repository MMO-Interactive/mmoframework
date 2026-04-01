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

$summary = [
    'Items' => count($items),
    'Skills' => count($skills),
    'Resources' => count($resources),
    'Nodes' => count($nodes),
    'Zones' => count($zones),
    'Live Sessions' => count($sessions),
    'Crafting Recipes' => count($recipes),
];

function render_status(array $result): string
{
    $ok = (bool)($result['ok'] ?? false);
    $status = safe_int($result['status'] ?? 0);
    $url = safe_text($result['url'] ?? '');
    $error = safe_text($result['error'] ?? '');
    $class = $ok ? 'status-ok' : 'status-error';

    if ($ok) {
        return sprintf('<li class="%s">%s <span>(HTTP %d)</span></li>', $class, htmlspecialchars($url), $status);
    }

    return sprintf('<li class="%s">%s <span>(%s)</span></li>', $class, htmlspecialchars($url), htmlspecialchars($error));
}
?>
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Sandbox MMO Wiki</title>
  <link rel="stylesheet" href="styles.css" />
</head>
<body>
  <header class="topbar">
    <h1>Sandbox MMO Wiki</h1>
    <p>Live documentation generated from dashboard APIs at <code><?= htmlspecialchars(api_base_url()) ?></code>.</p>
  </header>

  <main class="layout">
    <aside class="sidebar">
      <h2>Sections</h2>
      <nav>
        <a href="#overview">Overview</a>
        <a href="#api-health">API Health</a>
        <a href="#zones">Zones</a>
        <a href="#items">Items</a>
        <a href="#skills">Skills</a>
        <a href="#resources">Resources</a>
        <a href="#crafting">Crafting</a>
        <a href="#operations">Operations</a>
        <a href="#analytics">Analytics</a>
      </nav>
    </aside>

    <section class="content">
      <article id="overview" class="card">
        <h2>Overview</h2>
        <p>This wiki summarizes world, gameplay, and operations data from the running MMO control plane.</p>
        <div class="metric-grid">
          <?php foreach ($summary as $label => $value): ?>
            <div class="metric">
              <div class="metric-value"><?= safe_int($value) ?></div>
              <div class="metric-label"><?= htmlspecialchars($label) ?></div>
            </div>
          <?php endforeach; ?>
        </div>
      </article>

      <article id="api-health" class="card">
        <h2>API Health</h2>
        <ul class="status-list">
          <?= render_status($definitions) ?>
          <?= render_status($dashboard) ?>
          <?= render_status($crafting) ?>
          <?= render_status($analytics) ?>
        </ul>
      </article>

      <article id="zones" class="card">
        <h2>Zones</h2>
        <table>
          <thead><tr><th>ID</th><th>Name</th><th>Bounds X</th><th>Bounds Z</th><th>Active Players</th><th>Lifecycle</th></tr></thead>
          <tbody>
            <?php foreach ($zones as $zone):
              $active = 0;
              $lifecycle = '';
              foreach ($zoneRuntime as $runtime) {
                  if (safe_int($runtime['zoneId'] ?? 0) === safe_int($zone['zoneId'] ?? 0)) {
                      $active = safe_int($runtime['activePlayers'] ?? 0);
                      $lifecycle = safe_text($runtime['lifecycleState'] ?? '');
                      break;
                  }
              }
            ?>
              <tr>
                <td><?= safe_int($zone['zoneId'] ?? 0) ?></td>
                <td><?= htmlspecialchars(safe_text($zone['name'] ?? '')) ?></td>
                <td><?= htmlspecialchars(safe_text($zone['minX'] ?? '0')) ?> to <?= htmlspecialchars(safe_text($zone['maxX'] ?? '0')) ?></td>
                <td><?= htmlspecialchars(safe_text($zone['minZ'] ?? '0')) ?> to <?= htmlspecialchars(safe_text($zone['maxZ'] ?? '0')) ?></td>
                <td><?= $active ?></td>
                <td><?= htmlspecialchars($lifecycle) ?></td>
              </tr>
            <?php endforeach; ?>
          </tbody>
        </table>
      </article>

      <article id="items" class="card">
        <h2>Items</h2>
        <table>
          <thead><tr><th>ID</th><th>Name</th><th>Max Stack</th><th>Base Weight</th></tr></thead>
          <tbody>
            <?php foreach ($items as $item): ?>
              <tr>
                <td><code><?= htmlspecialchars(safe_text($item['id'] ?? '')) ?></code></td>
                <td><?= htmlspecialchars(safe_text($item['name'] ?? '')) ?></td>
                <td><?= safe_int($item['maxStack'] ?? 0) ?></td>
                <td><?= htmlspecialchars(safe_text($item['baseWeight'] ?? '0')) ?></td>
              </tr>
            <?php endforeach; ?>
          </tbody>
        </table>
      </article>

      <article id="skills" class="card">
        <h2>Skills</h2>
        <table>
          <thead><tr><th>ID</th><th>Name</th><th>Max Value</th></tr></thead>
          <tbody>
            <?php foreach ($skills as $skill): ?>
              <tr>
                <td><code><?= htmlspecialchars(safe_text($skill['id'] ?? '')) ?></code></td>
                <td><?= htmlspecialchars(safe_text($skill['name'] ?? '')) ?></td>
                <td><?= safe_int($skill['maxValue'] ?? 0) ?></td>
              </tr>
            <?php endforeach; ?>
          </tbody>
        </table>
      </article>

      <article id="resources" class="card">
        <h2>Resources & Nodes</h2>
        <h3>Resource Definitions</h3>
        <table>
          <thead><tr><th>ID</th><th>Name</th><th>Item</th><th>Base Yield</th></tr></thead>
          <tbody>
            <?php foreach ($resources as $resource): ?>
              <tr>
                <td><code><?= htmlspecialchars(safe_text($resource['id'] ?? '')) ?></code></td>
                <td><?= htmlspecialchars(safe_text($resource['name'] ?? '')) ?></td>
                <td><code><?= htmlspecialchars(safe_text($resource['itemId'] ?? '')) ?></code></td>
                <td><?= safe_int($resource['baseYield'] ?? 0) ?></td>
              </tr>
            <?php endforeach; ?>
          </tbody>
        </table>

        <h3>Node Instances</h3>
        <table>
          <thead><tr><th>ID</th><th>Zone</th><th>Resource</th><th>Position</th><th>Respawn(s)</th></tr></thead>
          <tbody>
            <?php foreach ($nodes as $node): ?>
              <tr>
                <td><code><?= htmlspecialchars(safe_text($node['id'] ?? '')) ?></code></td>
                <td><?= safe_int($node['zoneId'] ?? 0) ?></td>
                <td><code><?= htmlspecialchars(safe_text($node['resourceId'] ?? '')) ?></code></td>
                <td>(<?= htmlspecialchars(safe_text($node['positionX'] ?? '0')) ?>, <?= htmlspecialchars(safe_text($node['positionY'] ?? '0')) ?>, <?= htmlspecialchars(safe_text($node['positionZ'] ?? '0')) ?>)</td>
                <td><?= safe_int($node['respawnSeconds'] ?? 0) ?></td>
              </tr>
            <?php endforeach; ?>
          </tbody>
        </table>
      </article>

      <article id="crafting" class="card">
        <h2>Crafting Recipes</h2>
        <table>
          <thead><tr><th>Recipe</th><th>Output</th><th>Craft Seconds</th><th>Ingredients</th></tr></thead>
          <tbody>
            <?php foreach ($recipes as $recipe):
              $ingredients = is_array($recipe['ingredients'] ?? null) ? $recipe['ingredients'] : [];
              $parts = [];
              foreach ($ingredients as $ingredient) {
                  $parts[] = safe_int($ingredient['quantity'] ?? 0) . '× ' . safe_text($ingredient['itemId'] ?? '');
              }
            ?>
              <tr>
                <td><code><?= htmlspecialchars(safe_text($recipe['recipeId'] ?? '')) ?></code><br/><?= htmlspecialchars(safe_text($recipe['name'] ?? '')) ?></td>
                <td><?= safe_int($recipe['outputQuantity'] ?? 0) ?>× <code><?= htmlspecialchars(safe_text($recipe['outputItemId'] ?? '')) ?></code></td>
                <td><?= safe_int($recipe['craftSeconds'] ?? 0) ?></td>
                <td><?= htmlspecialchars(implode(', ', $parts)) ?></td>
              </tr>
            <?php endforeach; ?>
          </tbody>
        </table>
      </article>

      <article id="operations" class="card">
        <h2>Operations Snapshot</h2>
        <p>Gateway attempts: <strong><?= safe_int($dashData['gateway']['connectionAttempts'] ?? 0) ?></strong></p>
        <p>Gateway successful logins: <strong><?= safe_int($dashData['gateway']['successfulLogins'] ?? 0) ?></strong></p>
        <p>Gateway errors: <strong><?= safe_int($dashData['gateway']['errors'] ?? 0) ?></strong></p>
        <p>Sessions in memory: <strong><?= count($sessions) ?></strong></p>
      </article>

      <article id="analytics" class="card">
        <h2>Derived Analytics</h2>
        <p>This section reflects `/api/analytics/overview` from the dashboard.</p>
        <?php if ($analytics['ok']): ?>
          <div class="metric-grid">
            <div class="metric"><div class="metric-value"><?= safe_int($analyticsData['accounts']['totalAccounts'] ?? 0) ?></div><div class="metric-label">Total Accounts</div></div>
            <div class="metric"><div class="metric-value"><?= safe_int($analyticsData['accounts']['onlineAccounts'] ?? 0) ?></div><div class="metric-label">Online Accounts</div></div>
            <div class="metric"><div class="metric-value"><?= safe_int($analyticsData['sessions']['onlinePlayers'] ?? 0) ?></div><div class="metric-label">Online Players</div></div>
            <div class="metric"><div class="metric-value"><?= safe_int($analyticsData['combat']['actions24h'] ?? 0) ?></div><div class="metric-label">Combat Actions (24h)</div></div>
            <div class="metric"><div class="metric-value"><?= safe_int($analyticsData['combat']['damage24h'] ?? 0) ?></div><div class="metric-label">Damage (24h)</div></div>
            <div class="metric"><div class="metric-value"><?= safe_int($analyticsData['content']['craftingRecipes'] ?? 0) ?></div><div class="metric-label">Craft Recipes</div></div>
          </div>
        <?php else: ?>
          <p class="status-error">Analytics API unavailable: <?= htmlspecialchars(safe_text($analytics['error'] ?? 'unknown error')) ?></p>
        <?php endif; ?>
      </article>
    </section>
  </main>
</body>
</html>
