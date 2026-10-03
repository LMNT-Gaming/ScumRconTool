<?php
//home.php
if (empty($_SESSION['steamid'])) {
  exit('Bitte einloggen.');
}

require_once __DIR__ . '/../functions/scum_user_function.php';
require_once __DIR__ . '/../functions/server_news_function.php';
$newsList = news_list(5, true); // nur published

$steamId = (string)$_SESSION['steamid'];
$userProfile = scum_get_user_profile_by_steamid($steamId);

$userProfileId = (int)($userProfile['id'] ?? 0);
// Geld (Kuna = SCUM$)
$balances = scum_get_balances_by_user_profile_id($userProfileId);
$kuna = (int)($balances['kuna'] ?? 0);
$gold = (int)($balances['gold'] ?? 0);

// Template (STR/CON/DEX/INT + Skills)
$template = scum_parse_character_template($userProfile['template_xml'] ?? null);
// Live-Blob holen + versuchen zu extrahieren
$prisonerId = (int)($userProfile['prisoner_id'] ?? 0);
$bodyBlob   = scum_get_prisoner_body_blob_by_user_profile_id($userProfileId, $prisonerId);
$live       = scum_get_live_attributes_from_body_blob($bodyBlob);
// Stats
$eventsStats   = scum_get_events_stats_by_user_profile_id($userProfileId);
$survivalStats = scum_get_survival_stats_by_user_profile_id($userProfileId);

// Fame / Zeiten
$famePoints     = (float)($userProfile['fame_points'] ?? 0);
$lastLogoutTime = (string)($userProfile['last_logout_time'] ?? '');

// Performance
$shotsFired    = (int)($survivalStats['shots_fired'] ?? 0);
$headshots     = (int)($survivalStats['headshots'] ?? 0);
$locksPicked   = (int)($survivalStats['locks_picked'] ?? 0);
$puppetsKilled = (int)($survivalStats['puppets_killed'] ?? 0);
$deaths        = (int)($survivalStats['deaths'] ?? 0);

// kills ohne Event-Kills
$killsTotal    = (int)($survivalStats['kills'] ?? 0);
$eventKills    = (int)($eventsStats['enemy_kills'] ?? 0);
$killsWorld    = max(0, $killsTotal - $eventKills);
$vehicles = scum_get_locked_vehicles_by_user_profile_id($userProfileId);
$squad = scum_get_squad_by_user_profile_id($userProfileId);

// Anzeige-Variablen
$playerName = $template['name']
  ?? ($userProfile['name'] ?? null)
  ?? ($userProfile['fake_name'] ?? null)
  ?? 'Unbekannt';

$money = $kuna;
$playtime = (int)($userProfile['play_time'] ?? 0);


// Fallback nur falls irgendwas fehlt
$str = $live['str'] ?? $template['str'] ?? 0;
$con = $live['con'] ?? $template['con'] ?? 0;
$dex = $live['dex'] ?? $template['dex'] ?? 0;
$int = $live['int'] ?? $template['int'] ?? 0;


$age = $template['age'] ?? null;

// last_logout_time hübscher
$lastLogoutPretty = '—';
if ($lastLogoutTime !== '') {
  $ts = strtotime($lastLogoutTime);
  $lastLogoutPretty = $ts ? date('d.m.Y H:i', $ts) : $lastLogoutTime;
}

?>

<main class="content layout-3col">

  <!-- LEFT: STATISTICS -->
  <aside class="side left">
    <section class="panel panel-left">

      <div class="panel-topbar">
        <div class="panel-topbar-title">STATISTICS</div>
      </div>

      <!-- RADIALS -->
      <div class="stats-radials">
        <div class="radial">
          <?php $pStr = (int)round(max(0, min(5, (float)$str)) / 5 * 100); ?>
          <div class="radial-circle" style="--p: <?= $pStr ?>; --ring: rgba(255, 80, 80, 0.95);">
            <div class="radial-value"><?= number_format((float)$str, 1, '.', '') ?></div>
          </div>
          <div class="radial-label">STR</div>
        </div>

        <div class="radial">
          <?php $pStr = (int)round(max(0, min(5, (float)$con)) / 5 * 100); ?>
          <div class="radial-circle" style="--p: <?= $pStr ?>; --ring: rgba(80, 255, 109, 0.95);">
            <div class="radial-value"><?= number_format((float)$con, 1, '.', '') ?></div>
          </div>

          <div class="radial-label">CON</div>
        </div>

        <div class="radial">
          <?php $pStr = (int)round(max(0, min(5, (float)$dex)) / 5 * 100); ?>
          <div class="radial-circle" style="--p: <?= $pStr ?>; --ring: rgba(80, 255, 255, 0.95);">
            <div class="radial-value"><?= number_format((float)$dex, 1, '.', '') ?></div>
          </div>

          <div class="radial-label">DEX</div>
        </div>

        <div class="radial">
          <?php $pStr = (int)round(max(0, min(5, (float)$int)) / 5 * 100); ?>
          <div class="radial-circle" style="--p: <?= $pStr ?>; --ring: rgba(80,180,255,0.95);">
            <div class="radial-value"><?= number_format((float)$int, 1, '.', '') ?></div>
          </div>

          <div class="radial-label">INT</div>
        </div>
      </div>

      <!-- SKILLS GRID (placeholder) -->
      <div class="stats-skillgrid">
        <div class="skill-col">
          <div class="skill-head">STR</div>
          <?php
          $skillsStr = $template['skills']['STR'] ?? [];
          if (!$skillsStr) {
            $skillsStr = [
              ['label' => 'Brawling', 'level' => 0, 'xp' => 0],
              ['label' => 'Melee Weapons', 'level' => 0, 'xp' => 0],
              ['label' => 'Archery', 'level' => 0, 'xp' => 0],
            ];
          }
          foreach ($skillsStr as $s):
          ?>
            <div class="skill-row">
              <span><?= htmlspecialchars($s['label']) ?></span>
              <span>Lv <?= (int)$s['level'] ?></span>
            </div>
          <?php endforeach; ?>

        </div>

        <div class="skill-col">
          <div class="skill-head">CON</div>

          <?php
          $skillsCon = $template['skills']['CON'] ?? [];
          if (!$skillsCon) {
            $skillsCon = [
              ['label' => 'Running', 'level' => 0],
              ['label' => 'Endurance', 'level' => 0],
            ];
          }

          foreach ($skillsCon as $s):
          ?>
            <div class="skill-row">
              <span><?= htmlspecialchars($s['label']) ?></span>
              <span>Lv <?= (int)$s['level'] ?></span>
            </div>
          <?php endforeach; ?>
        </div>


        <div class="skill-col">
          <div class="skill-head">DEX</div>

          <?php
          $skillsDex = $template['skills']['DEX'] ?? [];
          if (!$skillsDex) {
            $skillsDex = [
              ['label' => 'Stealth', 'level' => 0],
              ['label' => 'Thievery', 'level' => 0],
              ['label' => 'Driving', 'level' => 0],
            ];
          }

          foreach ($skillsDex as $s):
          ?>
            <div class="skill-row">
              <span><?= htmlspecialchars($s['label']) ?></span>
              <span>Lv <?= (int)$s['level'] ?></span>
            </div>
          <?php endforeach; ?>
        </div>


        <div class="skill-col">
          <div class="skill-head">INT</div>

          <?php
          $skillsInt = $template['skills']['INT'] ?? [];
          if (!$skillsInt) {
            $skillsInt = [
              ['label' => 'Rifles', 'level' => 0],
              ['label' => 'Handgun', 'level' => 0],
              ['label' => 'Awareness', 'level' => 0],
            ];
          }

          foreach ($skillsInt as $s):
          ?>
            <div class="skill-row">
              <span><?= htmlspecialchars($s['label']) ?></span>
              <span>Lv <?= (int)$s['level'] ?></span>
            </div>
          <?php endforeach; ?>
        </div>

      </div>

      <!-- BASIC INFO -->
      <div class="panel-section">
        <div class="section-title">BASIC INFO</div>
        <div class="kv">
          <div class="kv-row"><span>Age</span><span><?= $age !== null ? (int)$age . ' years' : '—' ?></span></div>
          <div class="kv-row"><span>Lifetime</span><span><?= htmlspecialchars(scum_format_seconds_hms($playtime)) ?> h</span></div>
          <div class="kv-row"><span>Fame Points</span><span><?= number_format($famePoints, 0, ',', '.') ?></span></div>
          <div class="kv-row"><span>Last Logout</span><span><?= htmlspecialchars($lastLogoutPretty) ?></span></div>
        </div>
      </div>

      <!-- PERFORMANCE DATA -->
      <div class="panel-section">
        <div class="section-title">PERFORMANCE DATA</div>
        <div class="kv two-col">
          <div class="kv-row"><span>Shots Fired</span><span><?= number_format($shotsFired, 0, ',', '.') ?></span></div>
          <div class="kv-row"><span>Kills (World)</span><span><?= number_format($killsWorld, 0, ',', '.') ?></span></div>
          <div class="kv-row"><span>Headshots</span><span><?= number_format($headshots, 0, ',', '.') ?></span></div>
          <div class="kv-row"><span>Puppets Killed</span><span><?= number_format($puppetsKilled, 0, ',', '.') ?></span></div>
          <div class="kv-row"><span>Locks Picked</span><span><?= number_format($locksPicked, 0, ',', '.') ?></span></div>
          <div class="kv-row"><span>Deaths</span><span><?= number_format($deaths, 0, ',', '.') ?></span></div>
        </div>
      </div>

    </section>
  </aside>

  <!-- CENTER -->
  <section class="center">
    <div class="main-card">
      <div class="center-head">
        <h1>NEWS</h1>
      </div>

      <div class="center-body">

        <?php if (empty($newsList)): ?>
          <p class="muted">Keine News vorhanden.</p>
        <?php else: ?>
          <?php foreach ($newsList as $n): ?>
            <?php
            $datePretty = (string)($n['created_at'] ?? '');
            $ts = strtotime($datePretty);
            if ($ts) $datePretty = date('d.m.Y H:i', $ts);
            ?>

            <div class="scum-slot news-card" style="margin-bottom:10px;">
              <div style="display:flex; justify-content:space-between; gap:10px;">
                <div style="font-weight:800; letter-spacing:.4px;">
                  <?= htmlspecialchars((string)$n['title']) ?>
                </div>
                <div class="muted" style="white-space:nowrap;">
                  <?= htmlspecialchars($datePretty) ?>
                </div>
              </div>

              <?php
              $bodyText = trim((string)($n['body'] ?? ''));

              // discord_json -> Kategorien holen
              $cats = [];
              if (!empty($n['discord_json'])) {
                $tmp = json_decode((string)$n['discord_json'], true);
                if (is_array($tmp) && isset($tmp['categories']) && is_array($tmp['categories'])) {
                  $cats = $tmp['categories'];
                }
              }
              ?>

              <div style="margin-top:8px; color:#e5e7eb;">
                <?php if ($bodyText !== ''): ?>
                  <div style="white-space:pre-wrap;"><?= nl2br(htmlspecialchars($bodyText)) ?></div>

                <?php elseif (!empty($cats)): ?>
                  <?php foreach ($cats as $c): ?>
                    <?php
                    $cname = trim((string)($c['name'] ?? ''));
                    $items = $c['items'] ?? [];
                    if ($cname === '' || !is_array($items)) continue;
                    ?>
                    <?php
                    $ccolor = (string)($c['color'] ?? '#5865F2');
                    if (!preg_match('/^#[0-9a-fA-F]{6}$/', $ccolor)) $ccolor = '#5865F2';
                    ?>
                    <div style="margin-top:10px; padding-left:10px; border-left:4px solid <?= htmlspecialchars($ccolor) ?>;">
                      <div style="display:flex; align-items:center; gap:8px;">
                        <span style="width:10px; height:10px; border-radius:3px; background:<?= htmlspecialchars($ccolor) ?>; display:inline-block;"></span>
                        <div style="font-weight:900; letter-spacing:1px; text-transform:uppercase;">
                          <?= htmlspecialchars($cname) ?>
                        </div>
                      </div>
                      <ul style="margin:6px 0 0 18px; padding:0; opacity:.95;">
                        <?php foreach ($items as $it): ?>
                          <?php $it = trim((string)$it);
                          if ($it === '') continue; ?>
                          <li><?= htmlspecialchars($it) ?></li>
                        <?php endforeach; ?>
                      </ul>
                    </div>
                  <?php endforeach; ?>

                <?php else: ?>
                  <div class="muted">—</div>
                <?php endif; ?>
              </div>

            </div>
          <?php endforeach; ?>
        <?php endif; ?>

      </div>


    </div>
  </section>

  <!-- RIGHT -->
  <aside class="side right">
    <section class="panel panel-right">

      <div class="panel-topbar">
        <div class="panel-topbar-title">SQUAD</div>
      </div>



      <!-- Squad -->
      <div class="panel-box">


        <?php if ($squad): ?>
          <div class="kv">
            <div class="kv-row">
              <span>Name</span>
              <span><?= htmlspecialchars((string)$squad['name']) ?></span>
            </div>
            <div class="kv-row">
              <span>Mitglieder</span>
              <span><?= (int)$squad['members'] ?></span>
            </div>
            <div class="kv-row">
              <span>Rang</span>
              <span><?= htmlspecialchars((string)$squad['rank']) ?></span>
            </div>
            <div class="kv-row">
              <span>Fame</span>
              <span><?= number_format((int)$squad['fame'], 0, ',', '.') ?></span>
            </div>
          </div>
        <?php else: ?>
          <div class="kv muted">
            <div class="kv-row">
              <span>Kein Squad</span>
              <span>—</span>
            </div>
          </div>
        <?php endif; ?>
      </div>

      <div class="section-title">Fahrzeuge</div>
      <!-- Vehicles -->
      <div class="vehicle-list">
        <?php if (!empty($vehicles)): ?>
          <?php foreach ($vehicles as $v): ?>
            <div class="vehicle-item">
              <div class="vehicle-name"><?= htmlspecialchars((string)$v['name']) ?></div>
              <div class="vehicle-meta">
                ID: <?= htmlspecialchars((string)$v['id']) ?> • Letzter Zugriff: <?= htmlspecialchars((string)$v['last_access']) ?>
              </div>
            </div>
          <?php endforeach; ?>
        <?php else: ?>
          <div class="vehicle-item muted">
            <div class="vehicle-name">Keine verschlossenen Fahrzeuge</div>
            <div class="vehicle-meta">—</div>
          </div>
        <?php endif; ?>
      </div>





    </section>
  </aside>


</main>