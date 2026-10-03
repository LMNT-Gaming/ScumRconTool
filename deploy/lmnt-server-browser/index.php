<?php
declare(strict_types=1);
require __DIR__ . '/lib/storage.php';
$config = lmnt_config();
$servers = lmnt_read_servers();
usort($servers, static function (array $a, array $b): int {
    return (int)($b['last_seen'] ?? 0) <=> (int)($a['last_seen'] ?? 0);
});
$uniqueServers = [];
foreach ($servers as $server) {
    $ip = (string)($server['server_ip'] ?? '');
    if ($ip !== '' && !isset($uniqueServers[$ip])) $uniqueServers[$ip] = $server;
}
$servers = array_values($uniqueServers);
$now = time();
$onlineAfter = (int)$config['online_after_seconds'];
$activeCount = count(array_filter($servers, static function (array $server) use ($now, $onlineAfter): bool {
    return (int)($server['last_seen'] ?? 0) >= $now - $onlineAfter;
}));
$versions = array_values(array_unique(array_filter(array_map(static function (array $server): string {
    return (string)($server['tool_version'] ?? '');
}, $servers))));
function rr_h(string $value): string { return htmlspecialchars($value, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8'); }
function rr_ago(int $timestamp): string {
    $seconds = max(0, time() - $timestamp);
    if ($seconds < 60) return 'gerade eben';
    if ($seconds < 3600) return 'vor ' . (int)floor($seconds / 60) . ' Min.';
    if ($seconds < 86400) return 'vor ' . (int)floor($seconds / 3600) . ' Std.';
    return 'vor ' . (int)floor($seconds / 86400) . ' Tagen';
}
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');
$pageTitle = 'Red Raven SCUM Serverliste';
require dirname(__DIR__) . '/includes/header.php';
?>
<link rel="stylesheet" href="assets/server-browser.css">
<section class="rr-browser">
  <section class="card rr-hero">
    <div class="rr-hero__content">
      <div>
        <span class="badge">Red Raven RCON Tool</span>
        <h1 class="rr-title">Öffentliche SCUM Serverliste</h1>
        <p class="rr-copy">Hier findest du SCUM-Server, deren Betreiber die öffentliche Anzeige im Red Raven RCON Tool freiwillig aktiviert haben. Die Liste wird automatisch aktualisiert und zeigt keine Passwörter, Logs oder Spielerdaten.</p>
      </div>
      <div class="rr-actions">
        <a class="btn" href="https://github.com/LMNT-Gaming/ScumRconTool" target="_blank" rel="noopener noreferrer">Open Source auf GitHub</a>
        <a class="btn btn--ghost" href="privacy.php">Datenschutzhinweise</a>
      </div>
    </div>
  </section>

  <div class="rr-stats">
    <div class="card rr-stat"><span class="badge">Server</span><strong><?=count($servers)?></strong></div>
    <div class="card rr-stat"><span class="badge">Kürzlich aktiv</span><strong><?=$activeCount?></strong></div>
    <div class="card rr-stat"><span class="badge">Tool-Versionen</span><strong><?=count($versions)?></strong></div>
  </div>

  <section class="card rr-table-card" aria-labelledby="server-list-title">
    <div class="rr-table-wrap">
      <table class="rr-table">
        <thead><tr><th id="server-list-title">Server</th><th>SCUM-Server-IP</th><th>Tool-Version</th><th>Status</th><th>Zuletzt gesehen</th></tr></thead>
        <tbody>
        <?php if ($servers === []): ?>
          <tr><td class="rr-empty" colspan="5">Noch keine freiwillig gemeldeten Server vorhanden.</td></tr>
        <?php else: foreach ($servers as $server):
          $last = (int)($server['last_seen'] ?? 0);
          $online = $last >= $now - $onlineAfter;
        ?>
          <tr>
            <td><span class="rr-server-name"><?=rr_h((string)($server['server_name'] ?? 'SCUM Server'))?></span></td>
            <td><span class="rr-ip"><?=rr_h((string)($server['server_ip'] ?? ''))?></span></td>
            <td><?=rr_h((string)($server['tool_version'] ?? 'unknown'))?></td>
            <td><span class="rr-status <?=$online ? 'rr-status--online' : 'rr-status--offline'?>"><?=$online ? 'Aktiv' : 'Nicht kürzlich gesehen'?></span></td>
            <td title="<?=rr_h(gmdate('c', $last))?>"><?=rr_h(rr_ago($last))?></td>
          </tr>
        <?php endforeach; endif; ?>
        </tbody>
      </table>
    </div>
  </section>
</section>
<?php require dirname(__DIR__) . '/includes/footer.php'; ?>