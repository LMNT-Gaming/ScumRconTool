<?php
declare(strict_types=1);

require_once __DIR__ . '/../functions/scum_db.php';
$scum = scum_db_status();

$navItems = [
    ['page' => 'home', 'label' => 'Übersicht', 'index' => '01'],
    ['page' => 'map', 'label' => 'Serverkarte', 'index' => '02'],
    ['page' => 'quests_overview', 'label' => 'Quests', 'index' => '03'],
    ['page' => 'challenges', 'label' => 'Challenges', 'index' => '04'],
    ['page' => 'squad', 'label' => 'Squad', 'index' => '05'],
    ['page' => 'base', 'label' => 'Basis', 'index' => '06'],
    ['page' => 'stats', 'label' => 'Statistiken', 'index' => '07'],
    ['page' => 'gym', 'label' => 'Gym', 'index' => '08'],
    ['page' => 'casino', 'label' => 'Casino', 'index' => '09'],
    ['page' => 'insurance', 'label' => 'Versicherung', 'index' => '10'],
    ['page' => 'heavy_industries', 'label' => 'Heavy Industries', 'index' => '11'],
];

$pageContext = [
    'home' => ['Dashboard', 'Dein SCUM-Leben auf einen Blick.'],
    'map' => ['Serverkarte', 'Orientierung, Fahrzeuge und aktive Ziele.'],
    'quests_overview' => ['Quests', 'Aufgaben entdecken und Fortschritt planen.'],
    'challenges' => ['Challenges', 'Dein persönlicher Stand und die gemeinsamen Serverziele.'],
    'squad' => ['Squad', 'Team, Mitglieder und gemeinsame Fahrzeuge.'],
    'base' => ['Basis', 'Bestände und Container übersichtlich verwalten.'],
    'stats' => ['Statistiken', 'Leistung und Fortschritt im Vergleich.'],
    'gym' => ['Gym', 'Charakterentwicklung und Training.'],
    'casino' => ['Casino', 'SCUM Slots, Blackjack und deine Casino-Chips.'],
    'insurance' => ['Fahrzeugversicherung', 'Deine Verträge, Laufzeiten und verfügbaren Ersatzfahrzeuge.'],
    'heavy_industries' => ['Heavy Industries', 'Stationäre Geräte für Basis und Werkstatt anfragen.'],
    'admin' => ['Admincenter', 'Serververwaltung und Moderation.'],
];
$context = $pageContext[$currentPage] ?? ['SCUM Usercenter', 'Deine persönliche LMNT-Schaltzentrale.'];
?>
<!doctype html>
<html lang="de">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex,nofollow,noarchive">
  <meta name="theme-color" content="#08070c">
  <title><?= htmlspecialchars($context[0]) ?> · LMNT SCUM Usercenter</title>
  <link rel="icon" href="../favicon.ico">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Archivo+Black&family=DM+Sans:opsz,wght@9..40,400;9..40,500;9..40,700&display=swap" rel="stylesheet">
  <link rel="stylesheet" href="assets/theme.css">
  <link rel="stylesheet" href="assets/style.css">
  <link rel="stylesheet" href="assets/modern.css?v=<?= (int) @filemtime(__DIR__ . '/../assets/modern.css') ?>">
  <?php if ($currentPage === 'casino'): ?><link rel="stylesheet" href="Casino/assets/casino.css?v=<?= (int) @filemtime(__DIR__ . '/../Casino/assets/casino.css') ?>"><?php endif; ?>
  <?php if ($currentPage === 'heavy_industries'): ?><link rel="stylesheet" href="assets/heavy-industries.css?v=<?= (int) @filemtime(__DIR__ . '/../assets/heavy-industries.css') ?>"><?php endif; ?>
</head>
<body class="scum-app page-<?= htmlspecialchars($currentPage) ?>">
<div class="app-shell">
  <?php if (!$scum['ok']): ?>
    <div id="scumSyncModal" class="scum-sync-modal open" role="dialog" aria-modal="true" aria-labelledby="sync-title">
      <div class="scum-sync-card warning">
        <div class="scum-sync-header"><div class="scum-sync-icon">!</div><div class="scum-sync-title" id="sync-title">Spieldaten werden aktualisiert</div></div>
        <div class="scum-sync-text">Einige Bereiche sind während der Übertragung kurzzeitig nicht verfügbar.</div>
        <div class="scum-sync-actions">
          <button class="btn btn-warning" type="button" onclick="location.reload()">Neu laden</button>
          <button class="btn btn-ghost" type="button" data-sync-close>Später</button>
        </div>
        <div class="scum-sync-sub muted">Grund: <?= htmlspecialchars((string) $scum['reason']) ?></div>
      </div>
    </div>
  <?php endif; ?>

  <header class="app-topbar">
    <div class="app-brand-row">
      <button class="nav-burger" id="navBurger" type="button" aria-label="Menü öffnen" aria-expanded="false" aria-controls="appSidebar"><span></span><span></span></button>
      <a class="app-brand" href="index.php?page=home" aria-label="SCUM Usercenter Startseite">
        <span class="app-brand-mark"><img src="../assets/img/logo.png" alt=""></span>
        <span><strong>LMNT</strong><small>SCUM USERCENTER</small></span>
      </a>
    </div>

    <div class="header-stats" aria-label="Spielerkonten">
      <?php foreach ([
        ['key' => 'fame', 'label' => 'Fame', 'unit' => 'FP'],
        ['key' => 'gold', 'label' => 'Gold', 'unit' => 'AU'],
        ['key' => 'kuna', 'label' => 'Scummies', 'unit' => '$'],
      ] as $stat): ?>
        <div class="stat stat-<?= htmlspecialchars($stat['key']) ?>">
          <span class="stat-icon"><img src="assets/img/icons/<?= htmlspecialchars($stat['key']) ?>.png" alt=""></span>
          <span class="stat-copy">
            <small><?= htmlspecialchars($stat['label']) ?></small>
            <span class="stat-value"><?= number_format((int) ($headerStats[$stat['key']] ?? 0), 0, ',', '.') ?><em><?= htmlspecialchars($stat['unit']) ?></em></span>
          </span>
        </div>
      <?php endforeach; ?>
    </div>

    <div class="app-top-actions">
      <a href="https://discord.gg/EYhabuyt5T" target="_blank" rel="noopener">Discord</a>
      <?php if (!empty($_SESSION['steamid'])): ?><a class="logout-link" href="auth/logout.php">Logout</a><?php endif; ?>
    </div>
  </header>

  <div class="sidebar-backdrop" data-sidebar-close></div>
  <aside class="app-sidebar" id="appSidebar" aria-label="Usercenter Navigation">
    <div class="sidebar-head">
      <span>PLAYER HUB</span>
      <button class="nav-close" id="navClose" type="button" aria-label="Menü schließen">×</button>
    </div>
    <nav class="app-nav">
      <?php foreach ($navItems as $item): ?>
        <a class="nav-btn <?= $currentPage === $item['page'] ? 'active' : '' ?>" href="index.php?page=<?= htmlspecialchars($item['page']) ?>">
          <span><?= htmlspecialchars($item['index']) ?></span><strong><?= htmlspecialchars($item['label']) ?></strong><i>↗</i>
        </a>
      <?php endforeach; ?>
      <?php if (!empty($_SESSION['isAdmin'])): ?>
        <a class="nav-btn nav-btn--admin <?= $currentPage === 'admin' ? 'active' : '' ?>" href="index.php?page=admin"><span>12</span><strong>Admincenter</strong><i>↗</i></a>
      <?php endif; ?>
    </nav>
    <div class="sidebar-foot">
      <span class="service-state <?= $scum['ok'] ? 'is-online' : 'is-offline' ?>"><i></i><?= $scum['ok'] ? 'Spieldaten online' : 'Synchronisierung läuft' ?></span>
      <a href="../index.php">Zur LMNT Homepage ↗</a>
    </div>
  </aside>

  <div class="app-main">
    <section class="app-context" aria-labelledby="page-title">
      <div>
        <span class="context-kicker">SCUM / <?= htmlspecialchars(strtoupper($currentPage)) ?></span>
        <h1 id="page-title"><?= htmlspecialchars($context[0]) ?><b>.</b></h1>
        <p><?= htmlspecialchars($context[1]) ?></p>
      </div>
      <div class="context-status <?= $scum['ok'] ? 'is-online' : 'is-offline' ?>"><i></i><span><?= $scum['ok'] ? 'LIVE DATA' : 'SYNCING' ?></span></div>
    </section>
