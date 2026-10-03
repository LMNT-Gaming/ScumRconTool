<?php

declare(strict_types=1);

if (session_status() !== PHP_SESSION_ACTIVE) session_start();
if (empty($_SESSION['steamid'])) {
    http_response_code(401);
    exit('Bitte einloggen.');
}

require_once __DIR__ . '/../functions/public_stats_consent_function.php';
require_once __DIR__ . '/../functions/scum_db.php';

function h(string $s): string
{
    return htmlspecialchars($s, ENT_QUOTES);
}
function fmt_int($n): string
{
    return number_format((int)$n, 0, ',', '.');
}
function fmt_float($n, int $d = 2): string
{
    return number_format((float)$n, $d, ',', '.');
}
function fmt_km($meters): string
{
    return fmt_float(((float)$meters) / 1000.0, 2) . ' km';
}
function fmt_hours($seconds): string
{
    return fmt_float(((float)$seconds) / 3600.0, 1) . ' h';
}

$steamid = (string)$_SESSION['steamid'];
$notice = $error = '';

/** 1) Consent oben */
if ($_SERVER['REQUEST_METHOD'] === 'POST') {

    // aktuellen Zustand holen (VOR dem Speichern)
    $current = public_stats_get_consent($steamid);
    $lockedUntilTs = !empty($current['locked_until'])
        ? (strtotime((string)$current['locked_until']) ?: null)
        : null;

    $nowLocked = ($lockedUntilTs !== null && $lockedUntilTs > time());

    $newConsent  = !empty($_POST['consent']) ? 1 : 0;
    $newShowName = !empty($_POST['show_name']) ? 1 : 0;

    $oldConsent  = (int)($current['consent'] ?? 0);
    $oldShowName = (int)($current['show_name'] ?? 0);

    // Widerruf-Checks: consent 1->0 ODER show_name 1->0 ist gesperrt während lock
    $isRevokingConsent  = ($oldConsent === 1 && $newConsent === 0);
    $isRevokingShowName = ($oldShowName === 1 && $newShowName === 0);

    if ($nowLocked && ($isRevokingConsent || $isRevokingShowName)) {
        $error = 'Änderung erst nach 24h möglich. Widerruf ab: ' . date('d.m.Y H:i', $lockedUntilTs);
    } else {
        $res = public_stats_set_consent($steamid, (bool)$newConsent, (bool)$newShowName);
        if ($res['ok']) $notice = $res['msg'];
        else $error = $res['msg'];
    }
}


$consentState = public_stats_get_consent($steamid);
$lockedUntilTs = !empty($consentState['locked_until']) ? (strtotime((string)$consentState['locked_until']) ?: null) : null;

/** 2) SCUM DB */
$db = getScumDbOrNull();
$status = scum_db_status();


/** Helfer: Stats für eine Liste SteamIDs aus SCUM.db holen */
function scum_stats_fetch_all(SQLite3 $db, int $activeDays = 365, int $minFame = 1, int $limit = 500): array
{
    $activeWhere = '';
    if ($activeDays > 0) {
        $since = time() - ($activeDays * 86400);
        $activeWhere = " AND up.last_login_time >= $since ";
    }

    $sql = "
      SELECT
        up.user_id AS steamid,
        up.id AS user_profile_id,
        up.name AS name,
        COALESCE(up.fame_points,0) AS fame_points,
        COALESCE(up.play_time,0) AS play_time,

        COALESCE(es.events_won,0) AS events_won,
        COALESCE(es.enemy_kills,0) AS event_enemy_kills,
        COALESCE(es.deaths,0) AS event_deaths,

        COALESCE(ss.headshots,0) AS headshots,
        COALESCE(ss.shots_fired,0) AS shots_fired,
        COALESCE(ss.puppets_killed,0) AS puppets_killed,
        COALESCE(ss.deaths,0) AS deaths_total,
        COALESCE(ss.containers_looted,0) AS containers_looted,
        COALESCE(ss.distance_travelled_by_foot,0) AS distance_travelled_by_foot,
        COALESCE(ss.distance_travelled_in_vehicle,0) AS distance_travelled_in_vehicle,
        COALESCE(ss.distance_travelled_swimming,0) AS distance_travelled_swimming,
        COALESCE(ss.distance_travel_by_boat,0) AS distance_travel_by_boat,

        up.last_login_time AS last_login
      FROM user_profile up
      LEFT JOIN events_stats es   ON es.user_profile_id = up.id
      LEFT JOIN survival_stats ss ON ss.user_profile_id = up.id
      WHERE 1=1
        AND COALESCE(up.fame_points,0) > $minFame
        $activeWhere
      ORDER BY COALESCE(up.fame_points,0) DESC
      LIMIT $limit
    ";

    $res = $db->query($sql);

    $players = [];
    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $sid = (string)$row['steamid'];
        $deaths_adj = max(0, (int)$row['deaths_total'] - (int)$row['event_deaths']);

        $players[$sid] = [
            'steamid' => $sid,
            'user_profile_id' => (int)$row['user_profile_id'],
            'name' => $row['name'] ?? 'Unknown',
            'fame_points' => (int)$row['fame_points'],
            'play_time' => (int)$row['play_time'],
            'headshots' => (int)$row['headshots'],
            'enemy_kills' => (int)$row['event_enemy_kills'],
            'events_won' => (int)$row['events_won'],
            'puppets_killed' => (int)$row['puppets_killed'],
            'shots_fired' => (int)$row['shots_fired'],
            'deaths_adj' => (int)$deaths_adj,
            'containers_looted' => (int)$row['containers_looted'],
            'distance_travelled_by_foot' => (float)$row['distance_travelled_by_foot'],
            'distance_travelled_in_vehicle' => (float)$row['distance_travelled_in_vehicle'],
            'distance_travel_by_boat' => (float)$row['distance_travel_by_boat'],
            'distance_travelled_swimming' => (float)$row['distance_travelled_swimming'],
            'last_login' => (int)$row['last_login'],
        ];
    }
    return $players;
}


/** 3) Meine Stats + Public Stats laden (nur wenn SCUM DB ok) */
$my = null;
$publicPlayers = [];
$publicAllowed = public_stats_list_allowed_steamids(); // [steamid => show_name]

$activeDays = isset($_GET['active']) ? (int)$_GET['active'] : 365;
$limit      = max(10, (int)($_GET['limit'] ?? 200));

if ($db && $status['ok']) {
    $publicPlayers = scum_stats_fetch_all($db, $activeDays, 1, $limit);

    foreach ($publicPlayers as $sid => &$p) {
        $hasConsent = array_key_exists($sid, $publicAllowed);              // consent=1?
        $showName   = $hasConsent && ((int)$publicAllowed[$sid] === 1);    // show_name=1?

        if (!$showName) {
            $p['name'] = 'Prisoner';
        }

        $p['_hasConsent'] = $hasConsent ? 1 : 0;
        $p['_showName']   = $showName ? 1 : 0;
    }
    unset($p);

    $my = $publicPlayers[$steamid] ?? null; // du bist ja in der Liste drin
}

?>
<style>
    .stats-consent {
        padding: 14px;
    }

    /* ===== Stats Table polish ===== */
    .stats-table {
        padding: 0;
        overflow: hidden;
    }

    .stats-table .box-title {
        padding: 12px 14px;
        margin: 0;
        border-bottom: 1px solid rgba(255, 255, 255, .08);
        background: rgba(0, 0, 0, .35);
    }

    /* wrapper: nicer scroll + rounded */
    .stats-table-wrapper {
        border-top: 1px solid rgba(255, 255, 255, .06);
        max-width: 100%;
        overflow-x: auto;
        overflow-y: hidden;
        direction: rtl;
        /* scrollbar oben (dein style) */
        background: rgba(0, 0, 0, .18);
    }

    .stats-table-wrapper table {
        direction: ltr;
        min-width: 1250px;
        border-collapse: collapse;
    }

    /* head */
    .stats-table-wrapper thead th {
        position: sticky;
        top: 0;
        z-index: 3;
        text-transform: uppercase;
        letter-spacing: 1.2px;
        font-size: 11px;
        font-weight: 900;
        color: rgba(255, 255, 255, .85);
        background: rgba(0, 0, 0, .55);
        border-bottom: 1px solid rgba(255, 255, 255, .10);
        padding: 10px 12px;
    }

    /* cells */
    .stats-table-wrapper td {
        padding: 10px 12px;
        border-bottom: 1px solid rgba(255, 255, 255, .06);
        border-right: 1px solid rgba(255, 255, 255, .04);
        white-space: nowrap;
        vertical-align: middle;
    }

    /* zebra rows (subtle) */
    .stats-table-wrapper tbody tr:nth-child(odd) td {
        background: rgba(255, 255, 255, .04);
    }

    .stats-table-wrapper tbody tr:nth-child(even) td {
        background: rgba(255, 255, 255, .02);
    }

    /* hover */
    .stats-table-wrapper tbody tr:hover td {
        filter: brightness(1.08);
    }

    /* sticky first 2 columns (rank + name) */
    .stats-table-wrapper th:first-child,
    .stats-table-wrapper td:first-child {
        position: sticky;
        left: 0;
        z-index: 4;
        background: rgba(0, 0, 0, .70);
        border-right: 1px solid rgba(255, 255, 255, .10);
    }

    .stats-table-wrapper th:nth-child(2),
    .stats-table-wrapper td:nth-child(2) {
        position: sticky;
        left: 56px;
        /* Breite der # Spalte */
        z-index: 4;
        background: rgba(0, 0, 0, .62);
        border-right: 1px solid rgba(255, 255, 255, .10);
    }

    /* rank column width */
    .stats-table-wrapper th:first-child,
    .stats-table-wrapper td:first-child {
        width: 56px;
        min-width: 56px;
        text-align: left;
    }

    /* name cell: nicer typography */
    .stats-table-wrapper td[data-key="name"] span {
        font-weight: 900;
        letter-spacing: .4px;
    }

    .stats-table-wrapper td[data-key="name"] .muted {
        opacity: .65;
    }

    /* me-row highlight: classy */
    .stats-table-wrapper tbody tr.me-row td {
        background: rgba(204, 0, 0, .14) !important;
        border-bottom-color: rgba(204, 0, 0, .25);
    }

    .stats-table-wrapper tbody tr.me-row td:first-child,
    .stats-table-wrapper tbody tr.me-row td:nth-child(2) {
        background: rgba(204, 0, 0, .22) !important;
    }

    /* badges tighter */
    .stats-table .status-pill {
        padding: 2px 8px;
        font-size: 10px;
        border-radius: 999px;
    }

    /* responsive: smaller paddings on mobile */
    @media (max-width:820px) {
        .stats-table-wrapper table {
            min-width: 1100px;
        }

        .stats-table-wrapper td,
        .stats-table-wrapper thead th {
            padding: 9px 10px;
        }
    }

    /* ===== FIX: Sticky columns need solid background ===== */

    /* Rank + Name Spalte IMMER voll deckend */
    .stats-table-wrapper th:first-child,
    .stats-table-wrapper td:first-child,
    .stats-table-wrapper th:nth-child(2),
    .stats-table-wrapper td:nth-child(2) {
        background-color: #1a1a1a !important;
        /* exakt wie panel-bg */
        backdrop-filter: none !important;
    }

    /* leichte Abtrennung nach rechts */
    .stats-table-wrapper th:first-child,
    .stats-table-wrapper td:first-child {
        box-shadow: 1px 0 0 rgba(255, 255, 255, .12);
    }

    .stats-table-wrapper th:nth-child(2),
    .stats-table-wrapper td:nth-child(2) {
        box-shadow: 1px 0 0 rgba(255, 255, 255, .08);
    }

    /* hover darf NICHT durchscheinen */
    .stats-table-wrapper tbody tr:hover td:first-child,
    .stats-table-wrapper tbody tr:hover td:nth-child(2) {
        background-color: #1f1f1f !important;
    }

    /* me-row bleibt deutlich */
    .stats-table-wrapper tbody tr.me-row td:first-child,
    .stats-table-wrapper tbody tr.me-row td:nth-child(2) {
        background-color: rgba(204, 0, 0, .35) !important;
    }

    /* ===== Top horizontal scrollbar for stats table ===== */

    .stats-table-scroll {
        position: relative;
    }

    /* oberer Scrollbalken */
    .stats-table-scroll-top {
        overflow-x: auto;
        overflow-y: hidden;
        height: 14px;
        background: rgba(0, 0, 0, .35);
        border-bottom: 1px solid rgba(255, 255, 255, .08);
    }

    /* Breite erzwingen = Tabellenbreite */
    .stats-table-scroll-top::before {
        content: "";
        display: block;
        width: 1250px;
        /* MUSS >= min-width der Tabelle sein */
        height: 1px;
    }

    /* eigentliche Tabelle */
    .stats-table-wrapper {
        overflow-x: auto;
        overflow-y: hidden;
    }

    .stats-table-wrapper {
        scrollbar-width: none;
    }

    .stats-table-wrapper::-webkit-scrollbar {
        height: 0;
    }
    .stats-table-wrapper {
  /* direction: rtl; <-- RAUS */
  direction: ltr;
}
.stats-table-wrapper table{
  direction: ltr;
}
.stats-table-scroll-top{
  overflow-x: auto;
  overflow-y: hidden;
  height: 14px;
  background: rgba(0,0,0,.35);
  border-bottom: 1px solid rgba(255,255,255,.08);
}

.stats-table-scroll-spacer{
  height: 1px;
  width: 0; /* wird per JS gesetzt */
}
.stats-table-wrapper{
  scrollbar-width: none;
}
.stats-table-wrapper::-webkit-scrollbar{
  height: 0;
}

</style>
<main class="content layout-3col">

    <!-- LEFT -->
    <aside class="side left">
        <section class="panel panel-left">
            <div class="panel-topbar">
                <div class="panel-topbar-title">STATS</div>
            </div>

            <div class="panel-body">
                <div class="panel-box">
                    <div class="box-title">Info</div>
                    <div class="kv">
                        <div class="kv-row"><span>Öffentlich sichtbar</span><span><?= (int)count($publicAllowed) ?></span></div>
                        <div class="kv-row"><span>Mein Status</span><span><?= ((int)$consentState['consent'] === 1 ? 'Public' : 'Privat') ?></span></div>
                    </div>
                </div>

                <?php if ($notice): ?>
                    <div class="panel-box">
                        <div class="box-title">OK</div>
                        <div><?= h($notice) ?></div>
                    </div>
                <?php endif; ?>

                <?php if ($error): ?>
                    <div class="panel-box">
                        <div class="box-title">Fehler</div>
                        <div><?= h($error) ?></div>
                    </div>
                <?php endif; ?>

            </div>
        </section>
    </aside>

    <!-- CENTER -->
    <section class="center">
        <section class="panel">
            <div class="panel-topbar">
                <div class="panel-topbar-title">MEINE STATS + PUBLIC</div>
            </div>

            <div class="panel-body">

                <!-- Consent (oben) -->
                <div class="panel-box">
                    <div class="box-title">Stats teilen</div>

                    <form method="post" style="display:grid; gap:10px;">
                        <label style="display:none; gap:10px; align-items:center;">
                            <input type="checkbox" name="consent" value="1" <?= ((int)$consentState['consent'] === 1 ? 'checked' : '') ?>>
                            <span>Ich willige ein, dass meine Stats öffentlich angezeigt werden.</span>
                        </label>

                        <label style="display:flex; gap:10px; align-items:center; margin-left:28px;">
                            <input type="checkbox" name="show_name" value="1" <?= ((int)$consentState['show_name'] === 1 ? 'checked' : '') ?>>
                            <span>Meinen Ingame-Namen anzeigen (sonst anonym)</span>
                        </label>

                        <div class="muted" style="margin-left:28px; font-size:12px;">
                            <?php if ((int)$consentState['consent'] === 1 && $lockedUntilTs): ?>
                                Widerruf erst möglich ab: <b><?= h(date('d.m.Y H:i', $lockedUntilTs)) ?></b>
                            <?php else: ?>
                                Wenn du aktivierst, ist ein Widerruf erst nach 24h möglich.
                            <?php endif; ?>
                        </div>

                        <div class="stats-form-actions" style="justify-content:flex-start;">
                            <button class="subtab active" type="submit" style="border-radius:12px;">Speichern</button>
                        </div>
                    </form>
                </div>

                <!-- Meine Stats -->
                <div class="panel-box">
                    <div class="box-title">Meine Stats</div>

                    <?php if (!$db || !$status['ok']): ?>
                        <div class="muted">SCUM.db aktuell nicht verfügbar: <?= h((string)$status['reason']) ?></div>
                    <?php elseif (!$my): ?>
                        <div class="muted">Keine Daten gefunden.</div>
                    <?php else: ?>
                        <div class="kv two-col">
                            <div class="kv-row"><span>Fame</span><span><?= (int)$my['fame_points'] ?></span></div>
                            <div class="kv-row"><span>Playtime</span><span><?= (int)$my['play_time'] ?></span></div>
                            <div class="kv-row"><span>Headshots</span><span><?= (int)$my['headshots'] ?></span></div>
                            <div class="kv-row"><span>Enemy Kills</span><span><?= (int)$my['enemy_kills'] ?></span></div>
                            <div class="kv-row"><span>Events Won</span><span><?= (int)$my['events_won'] ?></span></div>
                            <div class="kv-row"><span>Deaths (ohne Events)</span><span><?= (int)$my['deaths_adj'] ?></span></div>
                        </div>
                    <?php endif; ?>
                </div>

                <!-- Public Tabelle -->
                <div class="panel-box stats-table" style="height: 400px; overflow: auto;">
                    <div class="box-title" style="display:flex; justify-content:space-between; align-items:center;">
                        <span>Public Stats (nur Opt-In)</span>
                        <span class="status-pill status-approved">Spieler: <?= (int)count($publicAllowed) ?></span>
                    </div>

                    <?php if (!$db || !$status['ok']): ?>
                        <div class="muted">SCUM.db aktuell nicht verfügbar.</div>
                    <?php elseif (empty($publicPlayers)): ?>
                        <div class="muted">Noch hat niemand eingewilligt.</div>
                    <?php else: ?>

                        <div class="stats-table-scroll">
                            <div class="stats-table-scroll-top" aria-hidden="true">
                                <div class="stats-table-scroll-spacer"></div>
                            </div>
                            <div class="table-wrapper stats-table-wrapper">

                                <table class="table w-full text-sm">
                                    <thead>
                                        <tr>
                                            <th class="text-left">#</th>
                                            <th class="text-left">Spieler</th>
                                            <th class="text-right">Fame</th>
                                            <th class="text-right">Playtime</th>
                                            <th class="text-right">Kills</th>
                                            <th class="text-right">Headshots</th>
                                            <th class="text-right">Deaths</th>
                                            <th class="text-right">Gelootet</th>
                                            <th class="text-right">Zu Fuß</th>
                                            <th class="text-right">Fahrzeug</th>
                                            <th class="text-right">Boot</th>
                                            <th class="text-right">Swim</th>
                                        </tr>
                                    </thead>

                                    <tbody>
                                        <?php
                                        $rank = 0;
                                        foreach ($publicPlayers as $sid => $p):
                                            $rank++;
                                            $isMe = ($sid === $steamid);
                                            $hasConsent = !empty($p['_hasConsent']);
                                            $showName   = !empty($p['_showName']);
                                        ?>
                                            <tr class="<?= $isMe ? 'me-row' : '' ?>">
                                                <td data-key="rank" data-val="<?= $rank ?>">#<?= $rank ?></td>

                                                <td data-key="name" data-val="<?= h((string)$p['name']) ?>">
                                                    <div style="display:flex; align-items:center; gap:8px;">
                                                        <span style="font-weight:900; letter-spacing:.4px;"><?= h((string)$p['name']) ?></span>
                                                        <?php if ($hasConsent): ?>
                                                            <?php if ($showName): ?>
                                                                <span class="status-pill status-approved">PUBLIC</span>
                                                            <?php else: ?>
                                                                <span class="status-pill status-cancelled">ANONYM</span>
                                                            <?php endif; ?>
                                                        <?php else: ?>
                                                            <span class="status-pill status-pending">PRIVAT</span>
                                                        <?php endif; ?>
                                                    </div>
                                                    <div class="muted" style="font-size:11px; margin-top:2px;">
                                                        ID: <?= h((string)$p['user_profile_id']) ?>
                                                    </div>
                                                </td>

                                                <td class="text-right" data-key="fame_points" data-val="<?= (int)$p['fame_points'] ?>"><?= fmt_int($p['fame_points']) ?></td>
                                                <td class="text-right" data-key="play_time" data-val="<?= (int)$p['play_time'] ?>"><?= fmt_hours($p['play_time']) ?></td>

                                                <td class="text-right" data-key="enemy_kills" data-val="<?= (int)$p['enemy_kills'] ?>"><?= fmt_int($p['enemy_kills']) ?></td>
                                                <td class="text-right" data-key="headshots" data-val="<?= (int)$p['headshots'] ?>"><?= fmt_int($p['headshots']) ?></td>
                                                <td class="text-right" data-key="deaths_adj" data-val="<?= (int)$p['deaths_adj'] ?>"><?= fmt_int($p['deaths_adj']) ?></td>

                                                <td class="text-right" data-key="containers_looted" data-val="<?= (int)$p['containers_looted'] ?>"><?= fmt_int($p['containers_looted']) ?></td>

                                                <td class="text-right" data-key="distance_travelled_by_foot" data-val="<?= (float)$p['distance_travelled_by_foot'] ?>">
                                                    <?= fmt_km($p['distance_travelled_by_foot']) ?>
                                                </td>

                                                <td class="text-right" data-key="distance_travelled_in_vehicle" data-val="<?= (float)$p['distance_travelled_in_vehicle'] ?>">
                                                    <?= fmt_km($p['distance_travelled_in_vehicle']) ?>
                                                </td>

                                                <td class="text-right" data-key="distance_travel_by_boat" data-val="<?= (float)$p['distance_travel_by_boat'] ?>">
                                                    <?= fmt_km($p['distance_travel_by_boat']) ?>
                                                </td>

                                                <td class="text-right" data-key="distance_travelled_swimming" data-val="<?= (float)$p['distance_travelled_swimming'] ?>">
                                                    <?= fmt_km($p['distance_travelled_swimming']) ?>
                                                </td>
                                            </tr>
                                        <?php endforeach; ?>
                                    </tbody>
                                </table>
                            </div>
                        </div>
                </div>

            <?php endif; ?>
            </div>


            </div>
        </section>
    </section>

   

</main>
<script>
(() => {
  const topScroll = document.querySelector('.stats-table-scroll-top');
  const spacer    = document.querySelector('.stats-table-scroll-spacer');
  const tableWrap = document.querySelector('.stats-table-wrapper');
  const table     = tableWrap?.querySelector('table');
  if (!topScroll || !spacer || !tableWrap || !table) return;

  function syncWidth(){
    spacer.style.width = table.scrollWidth + 'px';
  }

  let syncing = false;

  topScroll.addEventListener('scroll', () => {
    if (syncing) return;
    syncing = true;
    tableWrap.scrollLeft = topScroll.scrollLeft;
    syncing = false;
  });

  tableWrap.addEventListener('scroll', () => {
    if (syncing) return;
    syncing = true;
    topScroll.scrollLeft = tableWrap.scrollLeft;
    syncing = false;
  });

  syncWidth();
  window.addEventListener('resize', syncWidth);
})();
</script>
