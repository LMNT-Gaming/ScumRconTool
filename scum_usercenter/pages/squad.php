<?php
//squad.php
declare(strict_types=1);

if (empty($_SESSION['steamid'])) exit('Bitte einloggen.');
require_once __DIR__ . '/../functions/scum_user_function.php';

$steamId = (string)$_SESSION['steamid'];
$userProfile = scum_get_user_profile_by_steamid($steamId);
$userProfileId = (int)($userProfile['id'] ?? 0);

$squad = scum_get_squad_by_user_profile_id($userProfileId);
// Squad Fahrzeuge (locked) sammeln
$squadVehicles = scum_get_locked_vehicles_by_squad_id((int)$squad['id']);

// Wenn kein Squad: früh raus
if (!$squad) {
  echo '<main class="content layout-3col"><section class="center"><div class="main-card"><h1>Squad</h1><p class="muted">Du bist in keinem Squad.</p></div></section></main>';
  exit;
}

// Mitglieder laden (NEU – Funktion unten in scum_user_function.php ergänzen)
$members = scum_get_squad_members_by_squad_id((int)$squad['id']); // name, rank, fame, playtime
?>

<main class="content layout-3col">

  <!-- LEFT -->
  <aside class="side left">
    <section class="panel panel-left">
      <div class="panel-topbar">
        <div class="panel-topbar-title">SQUAD</div>
      </div>

      <div class="panel-body">
        <div class="panel-box">
          <div class="box-title"><?= htmlspecialchars((string)$squad['name']) ?></div>

          <div class="kv">
            <div class="kv-row"><span>Mitglieder</span><span><?= (int)$squad['members'] ?></span></div>
            <div class="kv-row"><span>Fame (Summe)</span><span><?= number_format((int)$squad['fame'], 0, ',', '.') ?></span></div>
            <div class="kv-row"><span>Dein Rang</span><span><?= htmlspecialchars((string)$squad['rank']) ?></span></div>
          </div>
        </div>
      </div>
    </section>
  </aside>

  <!-- CENTER -->
  <section class="center">
    <div class="main-card">

      <div class="panel-topbar-title" style='display: ruby;'>
        <div class="userblock">
          <div class="userlabel">Squad</div>
          <div class="username"><?= htmlspecialchars((string)$squad['name']) ?></div>
        </div>
        <div class="moneyblock">
          <div class="userlabel">Mitglieder</div>
          <div class="money"><?= (int)$squad['members'] ?></div>
        </div>
      </div>

      <div class="center-body">
        <h1>Mitglieder</h1>

        <div class="admin-table">
          <div class="admin-table-head">
            <div>Name</div>
            <div>Rang</div>
            <div>Fame</div>
            <div>Spielzeit</div>
          </div>

          <?php if (!empty($members)): ?>
            <?php foreach ($members as $m): ?>
              <div class="admin-table-row">
                <div><?= htmlspecialchars((string)$m['name']) ?></div>
                <div><?= (int)$m['rank'] ?></div>
                <div><?= number_format((int)$m['fame'], 0, ',', '.') ?></div>
                <div><?= htmlspecialchars(scum_format_seconds_hms((int)$m['playtime'])) ?></div>
              </div>
            <?php endforeach; ?>
          <?php else: ?>
            <div class="admin-table-row">
              <div class="muted">Keine Mitglieder gefunden</div>
              <div>—</div><div>—</div><div>—</div>
            </div>
          <?php endif; ?>
        </div>

      </div>
    </div>
  </section>

  <!-- RIGHT -->
  <aside class="side right">
    <section class="panel panel-right">
      <div class="panel-topbar">
        <div class="panel-topbar-title">SQUAD-FAHRZEUGE</div>
      </div>

      <div class="panel-body">
        <div class="vehicle-list">
      <?php if (!empty($squadVehicles)): ?>
        <?php foreach ($squadVehicles as $v): ?>
          <div class="vehicle-item">
            <div class="vehicle-name">
              <?= htmlspecialchars((string)$v['name']) ?>
            </div>
            <div class="vehicle-meta">
              Owner: <?= htmlspecialchars((string)$v['owner']) ?>
              • ID: <?= htmlspecialchars((string)$v['id']) ?>
              • Letzter Zugriff: <?= htmlspecialchars((string)$v['last_access']) ?>
            </div>
          </div>
        <?php endforeach; ?>
      <?php else: ?>
        <div class="vehicle-item muted">
          <div class="vehicle-name">Keine verschlossenen Squad-Fahrzeuge</div>
          <div class="vehicle-meta">—</div>
        </div>
      <?php endif; ?>
    </div>
      </div>

    </section>
  </aside>

</main>
