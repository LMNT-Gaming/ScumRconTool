<?php
// pages/gym.php
declare(strict_types=1);

if (session_status() !== PHP_SESSION_ACTIVE) session_start();
$steamId = (string)($_SESSION['steamid'] ?? '');

function gym_h(?string $s): string
{
    return htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
}
?>
<link rel="stylesheet" href="assets/gym.css?v=1">
<script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.7/dist/chart.umd.min.js" defer></script>
<script src="assets/gym.js?v=1" defer></script>

<main class="content gym-page">
  <div class="gym-shell" data-gym-root>
    <section class="gym-hero">
      <div>
        <div class="gym-kicker">SCUM Training Tracker</div>
        <h1>Gym</h1>
        <p>Verfolge deine Attribute und Skills automatisch ueber die GGCon-Schnittstelle. Neue Werte werden gespeichert, sobald du online bist und der Sync aktuelle Skilldaten findet.</p>
      </div>
      <div class="gym-status-card">
        <div class="gym-status-dot" data-sync-state></div>
        <div>
          <div class="gym-status-title" data-sync-title>Lade Daten...</div>
          <div class="gym-status-sub" data-sync-sub>SteamID: <?= gym_h($steamId) ?></div>
        </div>
      </div>
    </section>

    <section class="gym-grid gym-grid-stats">
      <div class="gym-card gym-stat-card">
        <span>Snapshots</span>
        <strong data-stat="snapshots">0</strong>
      </div>
      <div class="gym-card gym-stat-card">
        <span>Letzter Sync</span>
        <strong data-stat="sync">-</strong>
      </div>
      <div class="gym-card gym-stat-card">
        <span>Letzter Online-Treffer</span>
        <strong data-stat="seen">-</strong>
      </div>
      <div class="gym-card gym-stat-card">
        <span>Online Spieler</span>
        <strong data-stat="online">0</strong>
      </div>
    </section>

    <section class="gym-grid gym-grid-main">
      <article class="gym-card gym-chart-card">
        <div class="gym-card-head">
          <div>
            <h2>Attribute</h2>
            <p>STR, CON, DEX und INT im Verlauf.</p>
          </div>
        </div>
        <div class="gym-chart-wrap"><canvas id="gymAttrChart"></canvas></div>
      </article>

      <article class="gym-card gym-current-card">
        <div class="gym-card-head">
          <div>
            <h2>Aktueller Stand</h2>
            <p data-player-name>Noch keine Daten.</p>
          </div>
        </div>
        <div class="gym-attr-list" data-current-attrs></div>
      </article>
    </section>

    <section class="gym-card gym-chart-card">
      <div class="gym-card-head gym-skill-head">
        <div>
          <h2>Skill Entwicklung</h2>
          <p>Wähle einen Skill, um XP und Level zu vergleichen.</p>
        </div>
        <select class="gym-select" data-skill-select aria-label="Skill auswählen"></select>
      </div>
      <div class="gym-chart-wrap"><canvas id="gymSkillChart"></canvas></div>
    </section>

    <section class="gym-card gym-table-card">
      <div class="gym-card-head">
        <div>
          <h2>Alle Skills</h2>
          <p>Letzter gespeicherter Snapshot.</p>
        </div>
      </div>
      <div class="gym-table-wrap">
        <table class="gym-table">
          <thead><tr><th>Skill</th><th>Level</th><th>Levelname</th><th>XP</th><th>Delta XP</th></tr></thead>
          <tbody data-skill-table><tr><td colspan="5">Noch keine Skilldaten vorhanden.</td></tr></tbody>
        </table>
      </div>
    </section>
  </div>
</main>
