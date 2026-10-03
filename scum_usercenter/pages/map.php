<?php
if (empty($_SESSION['steamid'])) {
    http_response_code(401);
    exit('Bitte einloggen.');
}

require_once __DIR__ . '/../functions/quest_function.php';
$currentPage = 'map';

// ─────────────────────────────────────────────────────────────
// Vehicle Map Access (Website-DB: user_inventory)
// ─────────────────────────────────────────────────────────────
require_once __DIR__ . '/../functions/db_function.php';
require_once __DIR__ . '/../functions/env_function.php';
require_once __DIR__ . '/../functions/scum_db.php';
require_once __DIR__ . '/../functions/scum_user_function.php';
require_once __DIR__ . '/../functions/scum_squad_function.php'; // optional, wenn du es nutzen willst

load_env(__DIR__ . '/../private/.env');

$requiredItemName = (string)(getenv('REQUIRED_ITEM_NAME') ?: 'Fahrzeugkompass');


$configs = [];
$regions = [];
$dbOk = false;
$dbErr = '';
$steamid = (string)($_SESSION['steamid'] ?? '');
$VEH_ACCESS_GRANTED = false;
$vehicles = [];
$vehicleFreeCounts = [];
$vehicleFreeTotal  = 0;

$myProfile = null;
$myUpid = 0;

$squadId = 0;
$squadMembers = []; // [upid => name], OHNE mich

if ($steamid !== '') {
    $myProfile = scum_get_user_profile_by_steamid($steamid);
    $myUpid = (int)($myProfile['id'] ?? 0);

    if ($myUpid > 0) {
        $sq = scum_get_squad_by_user_profile_id($myUpid);
        $squadId = (int)($sq['id'] ?? 0);

        if ($squadId > 0) {
            // Squadmates holen (id + name)
            $db = getScumDb();
            $stmt = $db->prepare("
                SELECT u.id AS upid, u.name AS name
                FROM squad_member sm
                JOIN user_profile u ON sm.user_profile_id = u.id
                WHERE sm.squad_id = :sid
            ");
            $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);
            $res = $stmt->execute();

            if ($res) {
                while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
                    $upid = (int)($row['upid'] ?? 0);
                    if ($upid <= 0) continue;
                    if ($upid === $myUpid) continue; // mich raus
                    $squadMembers[$upid] = (string)($row['name'] ?? 'Unbekannt');
                }
            }
        }
    }
}


if ($steamid !== '') {
    $VEH_ACCESS_GRANTED = false;

    if ($steamid !== '') {
        try {
            $pdo = db(); // ← HIER der entscheidende Punkt
            $stmt = $pdo->prepare(
                "SELECT 1 
             FROM user_inventory 
             WHERE steamid = ? AND item_name = ? 
             LIMIT 1"
            );
            $stmt->execute([$steamid, $requiredItemName]);
            $VEH_ACCESS_GRANTED = (bool)$stmt->fetchColumn();
        } catch (Throwable $e) {
            $VEH_ACCESS_GRANTED = false;
            // optional: error_log($e->getMessage());
        }
    }
}

// ─────────────────────────────────────────────────────────────
// Vehicle Query helpers (Locks filtern)
// ─────────────────────────────────────────────────────────────
function isLockedFromItemXml(?string $xmlStr): bool
{
    if ($xmlStr === null) return false;
    $xmlStr = trim($xmlStr);
    if ($xmlStr === '') return false;

    libxml_use_internal_errors(true);
    $xml = simplexml_load_string($xmlStr);
    libxml_clear_errors();

    if ($xml === false) {
        $t = strtolower($xmlStr);
        if (strpos($t, '<locks') === false) return false;
        if (strpos($t, '<lockslot') !== false) return true;
        return false;
    }

    if (!isset($xml->Locks)) return false;
    $lockSlots = $xml->Locks->xpath('LockSlot');
    return (is_array($lockSlots) && count($lockSlots) > 0);
}


// Klassenfarben (wie bei dir)
$VEHICLE_CLASS_COLORS = [
    'CityBike_ES'          => '#0c69f5ff',
    'MountainBike_ES'      => '#c56618ff',
    'Dirtbike_ES'          => '#8d0000ff',
    'Cruiser_ES'           => '#9467bd',
    'WolfsWagen_ES'        => '#d62728',
    'Laika_ES'             => '#0e7e04ff',
    'Rager_ES'             => '#fcac00ff',
    'Tractor_ES'           => '#ff8080ff',
    'RIS_ES'               => '#ffee00ff',
    'WheelBarrow_Metal_ES' => '#535353ff',
    'SUP_ES'               => '#17becf',
    'Barba_ES'             => '#242424ff',
    'Kinglet_Duster_ES'    => '#fff7aaff',
    'Kinglet_Mariner_ES'   => '#33434eff',
];


/**
 * SCUM.db Pfad:
 * - Wenn du irgendwo zentral $scumDbPath setzt, wird der genutzt.
 * - Sonst Fallback auf ../SCUM.db (relativ zu /pages)
 */
if (!isset($scumDbPath) || !is_string($scumDbPath) || $scumDbPath === '') {
    $scumDbPath = __DIR__ . '/../scum_db/SCUM.db';
}



try {
    if (!is_file($scumDbPath)) {
        throw new RuntimeException('SCUM.db nicht gefunden unter: ' . $scumDbPath);
    }

    $scumDb = new SQLite3($scumDbPath, SQLITE3_OPEN_READONLY);
    $dbOk = true;
    // ─────────────────────────────────────────────────────────────
    // Free Vehicle Counts (IMMER: ohne Positionsdaten)
    //   "frei" = Vehicle ohne Locks
    // ─────────────────────────────────────────────────────────────
    if ($dbOk) {
        try {
            $resFree = $scumDb->query("
            SELECT
              e.class AS entity_class,
              COUNT(*) AS cnt
            FROM vehicle_entity ve
            JOIN entity e            ON e.id = ve.entity_id
            LEFT JOIN item_entity ie ON ie.entity_id = ve.item_container_entity_id
            WHERE
              ie.xml IS NULL
              OR ie.xml = ''
              OR (ie.xml NOT LIKE '%<Locks>%' AND ie.xml NOT LIKE '%<locks>%')
              OR (ie.xml LIKE '%<Locks>%' AND ie.xml NOT LIKE '%LockSlot%')
              OR (ie.xml LIKE '%<locks>%' AND ie.xml NOT LIKE '%lockslot%')
            GROUP BY e.class
            ORDER BY e.class
        ");

            $vehicleFreeCounts = [];
            $vehicleFreeTotal  = 0;

            if ($resFree) {
                while ($row = $resFree->fetchArray(SQLITE3_ASSOC)) {
                    $cls = (string)($row['entity_class'] ?? 'Unknown');
                    if ($cls === '') $cls = 'Unknown';
                    $cnt = (int)($row['cnt'] ?? 0);

                    $vehicleFreeCounts[$cls] = $cnt;
                    $vehicleFreeTotal += $cnt;
                }
            }
        } catch (Throwable $e) {
            // optional: error_log("free counts: ".$e->getMessage());
            $vehicleFreeCounts = [];
            $vehicleFreeTotal  = 0;
        }
    }
    $freeVehicles = [];

    if ($dbOk) {
        try {
            $stmtFree = $scumDb->prepare("
            SELECT
              ve.entity_id AS id,
              e.location_x AS pos_x,
              e.location_y AS pos_y,
              e.class      AS entity_class,
              ie.xml       AS xml_blob
            FROM vehicle_entity ve
            JOIN entity e            ON e.id = ve.entity_id
            LEFT JOIN item_entity ie ON ie.entity_id = ve.item_container_entity_id
            WHERE
              ie.xml IS NULL
              OR ie.xml = ''
              OR (ie.xml NOT LIKE '%<Locks>%' AND ie.xml NOT LIKE '%<locks>%')
              OR (ie.xml LIKE '%<Locks>%' AND ie.xml NOT LIKE '%LockSlot%')
              OR (ie.xml LIKE '%<locks>%' AND ie.xml NOT LIKE '%lockslot%')
        ");

            $res = $stmtFree->execute();
            if ($res) {
                while ($r = $res->fetchArray(SQLITE3_ASSOC)) {
                    $id  = (string)($r['id'] ?? '');
                    $x   = isset($r['pos_x']) ? (float)$r['pos_x'] : null;
                    $y   = isset($r['pos_y']) ? (float)$r['pos_y'] : null;
                    $cls = (string)($r['entity_class'] ?? 'Unknown');
                    if ($id === '' || $x === null || $y === null) continue;

                    $freeVehicles[] = [
                        'id'        => $id,
                        'x'         => $x,
                        'y'         => $y,
                        'class'     => $cls ?: 'Unknown',
                        'owner'     => 'free',     // <-- neu
                        'ownerName' => 'Frei',
                        'locked'    => false,
                    ];
                }
            }
        } catch (Throwable $e) {
            $freeVehicles = [];
            // optional error_log("free vehicles: ".$e->getMessage());
        }
    }

    // --- Zonen Konfigurationen ---
    $resCfg = $scumDb->query("
        SELECT id, map_id, name, color_red, color_green, color_blue
        FROM custom_zone_configuration
    ");

    if ($resCfg) {
        while ($row = $resCfg->fetchArray(SQLITE3_ASSOC)) {
            $r = (int)round(((float)$row['color_red'])   * 255);
            $g = (int)round(((float)$row['color_green']) * 255);
            $b = (int)round(((float)$row['color_blue'])  * 255);

            $configs[(int)$row['id']] = [
                'name' => (string)$row['name'],
                'r'    => $r,
                'g'    => $g,
                'b'    => $b,
                'fill' => sprintf('rgba(%d,%d,%d,0.40)', $r, $g, $b),
            ];
        }
    }

    // --- Zonen Regionen ---
    $resReg = $scumDb->query("
        SELECT id, map_id, name, location_x, location_y, size_x, size_y, configuration_index
        FROM custom_zone_region
    ");

    if ($resReg) {
        while ($z = $resReg->fetchArray(SQLITE3_ASSOC)) {
            $regions[] = [
                'id'        => (int)$z['id'],
                'name'      => (string)$z['name'],
                'x'         => (float)$z['location_x'],
                'y'         => (float)$z['location_y'],
                'size_x'    => (float)$z['size_x'],
                'size_y'    => (float)$z['size_y'],
                'config_id' => (int)$z['configuration_index'],
            ];
        }
    }
} catch (Throwable $e) {
    $dbOk = false;
    $dbErr = $e->getMessage();
}
// ─────────────────────────────────────────────────────────────
// Vehicles aus SCUM.db (JETZT: nachdem $scumDb existiert)
// ─────────────────────────────────────────────────────────────

$vehicles = [];               // wird jetzt OWNED vehicles enthalten
$vehicleCounts = [];          // counts für Legend
$vehicleTotal = 0;

if ($dbOk && $myUpid > 0) {
    try {
        // welche Profile dürfen wir sehen? (ich + squadmates)
        $allowedUpids = array_merge([$myUpid], array_keys($squadMembers));
        $allowedUpids = array_values(array_unique(array_filter(array_map('intval', $allowedUpids))));

        // Wenn du NUR "Dein Fahrzeug" willst, dann hier einfach: $allowedUpids = [$myUpid];

        // WHERE dynamisch bauen (je Profil-ID zwei Pattern: owningUserProfileId + _owningUserProfileId)
        $orParts = [];
        $bind = [];

        foreach ($allowedUpids as $i => $upid) {
            $k1 = ":ownU{$i}";
            $k2 = ":own{$i}";
            $orParts[] = "(ie.xml LIKE $k1 OR ie.xml LIKE $k2)";
            $bind[$k1] = '%_owningUserProfileId="' . $upid . '"%';
            $bind[$k2] = '%owningUserProfileId="' . $upid . '"%';
        }

        $whereOwn = $orParts ? ('(' . implode(' OR ', $orParts) . ')') : '0';

        $stmt = $scumDb->prepare("
            SELECT DISTINCT
                ve.entity_id AS id,
                e.location_x AS pos_x,
                e.location_y AS pos_y,
                e.class      AS entity_class,
                ie.xml       AS xml_blob
            FROM vehicle_entity ve
            JOIN entity e            ON e.id = ve.entity_id
            LEFT JOIN item_entity ie      ON ie.entity_id = ve.item_container_entity_id
            WHERE
                ie.xml IS NOT NULL AND ie.xml <> ''
                AND $whereOwn
        ");

        foreach ($bind as $k => $v) {
            $stmt->bindValue($k, $v, SQLITE3_TEXT);
        }

        $res = $stmt->execute();
        if ($res) {
            while ($r = $res->fetchArray(SQLITE3_ASSOC)) {
                $id  = (string)($r['id'] ?? '');
                $x   = isset($r['pos_x']) ? (float)$r['pos_x'] : null;
                $y   = isset($r['pos_y']) ? (float)$r['pos_y'] : null;
                $cls = (string)($r['entity_class'] ?? 'Unknown');
                $xml = (string)($r['xml_blob'] ?? '');

                if ($id === '' || $x === null || $y === null) continue;

                // Owner ermitteln (welche ID steckt im XML)
                $ownerType = 'other';
                $ownerName = '';
                $ownerUpid = 0;

                // self first
                if (strpos($xml, 'owningUserProfileId="' . $myUpid . '"') !== false || strpos($xml, '_owningUserProfileId="' . $myUpid . '"') !== false) {
                    $ownerType = 'self';
                    $ownerName = (string)($myProfile['name'] ?? 'Du');
                    $ownerUpid = $myUpid;
                } else {
                    foreach ($squadMembers as $upid => $name) {
                        $upid = (int)$upid;
                        if ($upid <= 0) continue;
                        if (strpos($xml, 'owningUserProfileId="' . $upid . '"') !== false || strpos($xml, '_owningUserProfileId="' . $upid . '"') !== false) {
                            $ownerType = 'squad';
                            $ownerName = (string)$name;
                            $ownerUpid = $upid;
                            break;
                        }
                    }
                }

                // Locked? (du hast die Funktion schon)
                $locked = isLockedFromItemXml($xml);

                $vehicles[] = [
                    'id'       => $id,
                    'x'        => $x,
                    'y'        => $y,
                    'class'    => $cls ?: 'Unknown',
                    'owner'    => $ownerType,   // self|squad
                    'ownerName' => $ownerName,   // tooltip
                    'locked'   => $locked,
                ];
                $vehicleCounts[$cls] = ($vehicleCounts[$cls] ?? 0) + 1;
                $vehicleTotal++;
            }
            $ownedSelf  = array_values(array_filter($vehicles, fn($v) => ($v['owner'] ?? '') === 'self'));
            $ownedSquad = array_values(array_filter($vehicles, fn($v) => ($v['owner'] ?? '') === 'squad'));
        }
    } catch (Throwable $e) {
        // optional: error_log($e->getMessage());
    }
}


// WICHTIG: Wenn kein Zugriff -> keine Daten an den Client
// Free positions immer zeigen, Owned positions nur bei Zugriff
// Self immer zeigen.
// Free + Squad nur wenn Zugriff (Item vorhanden)
// Owned immer mitgeben, Free nur wenn Item vorhanden
$ownedSelf  = $ownedSelf  ?? [];
$ownedSquad = $ownedSquad ?? [];

$vehiclesForClient = $VEH_ACCESS_GRANTED
    ? array_merge($ownedSelf, $ownedSquad, $freeVehicles)
    : array_merge($ownedSelf, $ownedSquad);

// Squad-UI nur aktiv, wenn Squad existiert UND Zugriff da ist
$squadHas = ($squadId > 0 && count($squadMembers) > 0);

$vehicleCountsForClient = $vehicleCounts;   // Counts sind ok
$vehicleTotalForClient  = $vehicleTotal;


$regionCount = count($regions);
$configCount = count($configs);

function h($s)
{
    return htmlspecialchars((string)$s, ENT_QUOTES, 'UTF-8');
}
?>
<style>
        .map-overlay {
            position: absolute;
            inset: 0;
            pointer-events: auto;
        }

        .map-inner {
            position: relative;
            width: 100%;
            max-width: 900px;
            /* <<< DAS ist der entscheidende Punkt */
            aspect-ratio: 1 / 1;
            margin: 0 auto;
            /* zentriert */
            border-radius: 16px;
            overflow: hidden;
            border: 1px solid rgba(15, 23, 42, 0.9);
            background: #020617;
        }

        .map-image {
            width: 100%;
            height: 100%;
            object-fit: contain;
            display: block;
            transform-origin: center center;
        }

        .map-toolbar {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 10px;
        }

        .map-zoom-btn {
            width: 36px;
            height: 36px;
            border-radius: 10px;
        }

        .map-caption {
            margin-top: 10px;
        }

        /* Tooltip */
        #zone-tooltip {
            position: fixed;
            padding: 0.25rem 0.45rem;
            border-radius: 6px;
            font-size: 0.75rem;
            background: rgba(15, 23, 42, 0.96);
            border: 1px solid rgba(148, 163, 184, 0.7);
            color: #e5e7eb;
            pointer-events: none;
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.7);
            z-index: 999;
            display: none;
        }

        .map-wrapper {
            display: flex;
            flex-direction: column;
            align-items: center;
            /* <<< zentriert Toolbar + Karte gemeinsam */
        }

        .map-toolbar {
            width: 100%;
            max-width: 900px;
            /* MUSS identisch zur map-inner sein */
            margin-bottom: 8px;
        }

        /* Leftbar darf nie höher als Viewport werden */
        .side.left {
            height: 80vh;
        }

        .side.right {
            height: 80vh;
        }

        .side.left .side.right .panel.panel-left .panel.panel-right {
            height: 80%;
            display: flex;
            flex-direction: column;
            min-height: 0;
            /* wichtig für flex scroll */
        }

        /* Der Inhaltsbereich soll scrollen */
        .side.left .panel-section {
            flex: 1;
            min-height: 0;
            overflow: hidden;
            /* wir scrollen innen */
        }

        /* Questliste scrollt */
        #questList {
            overflow: auto;
            max-height: 100%;
            padding-right: 4px;
        }

        /* optional: schöner Scrollbar (wenn du willst) */
        #questList::-webkit-scrollbar {
            width: 8px;
        }

        #questList::-webkit-scrollbar-thumb {
            background: rgba(148, 163, 184, .25);
            border-radius: 999px;
        }

        .map-hud {
            position: absolute;
            left: 12px;
            bottom: 12px;
            z-index: 50;
            display: flex;
            flex-direction: column;
            gap: 8px;
            pointer-events: auto;
        }

        .map-hud-row {
            display: flex;
            gap: 10px;
            align-items: center;
            flex-wrap: wrap;
        }

        /* ===== Map Toggle (SCUM Style) ===== */
        .map-toggle {
            display: flex;
            align-items: center;
            justify-content: space-between;
            /* passt gut für “Label links / Checkbox rechts” */
            gap: 10px;

            padding: 8px 10px;

            background: rgba(0, 0, 0, 0.45);
            border: 1px solid rgba(255, 255, 255, 0.08);

            color: #fff;
            font-family: consolas, system-ui, -apple-system, Segoe UI, Roboto, Arial, sans-serif;
            font-size: 12px;
            font-weight: 800;
            letter-spacing: 1.1px;
            text-transform: uppercase;

            user-select: none;
        }

        /* Checkbox: clean + passend */
        .map-toggle input[type="checkbox"] {
            width: 18px;
            height: 18px;
            accent-color: #fff;
            cursor: pointer;
        }

        /* Optional: Hover wie deine Buttons */
        .map-toggle:hover {
            transform: translateY(-2px);
            transition: transform .15s ease, border-color .15s ease, filter .15s ease;
            border-color: rgba(255, 255, 255, 0.18);
            filter: brightness(1.06);
        }

        /* ===== Access Badge (SCUM Style) ===== */
        .map-hud-badge {
            padding: 8px 10px;

            background: rgba(0, 0, 0, 0.35);
            border: 1px solid rgba(255, 120, 120, 0.55);

            color: #fff;
            font-family: consolas, system-ui, -apple-system, Segoe UI, Roboto, Arial, sans-serif;
            font-size: 12px;
            font-weight: 800;
            letter-spacing: 1px;
            text-transform: uppercase;
        }

        /* Optional: leicht “muted” Text innerhalb */
        .map-hud-badge b {
            color: #ffd7d7;
        }


        /* ===== Vehicle Legend (SCUM Style) ===== */
        .veh-legend {
            width: 100%;
            max-width: 320px;
            max-height: 220px;
            overflow: auto;

            background: rgba(0, 0, 0, 0.35);
            border: 1px solid rgba(255, 255, 255, 0.08);
            padding: 10px;
            display: grid;
            gap: 8px;

            font-family: consolas, system-ui, -apple-system, Segoe UI, Roboto, Arial, sans-serif;
            color: #fff;
        }

        /* Scrollbar wie deine Questlist */
        .veh-legend::-webkit-scrollbar {
            width: 8px;
        }

        .veh-legend::-webkit-scrollbar-thumb {
            background: rgba(148, 163, 184, .25);
            border-radius: 999px;
        }

        /* Row wie “Slot/Item” */
        .veh-legend .row {
            display: flex;
            align-items: center;
            gap: 10px;

            padding: 8px 10px;
            cursor: pointer;

            background: rgba(0, 0, 0, 0.45);
            border: 1px solid rgba(255, 255, 255, 0.08);

            font-size: 12px;
            font-weight: 800;
            letter-spacing: 1.1px;
            text-transform: uppercase;

            transition: transform .15s ease, border-color .15s ease, background .15s ease, filter .15s ease;
        }

        .veh-legend .row:hover {
            transform: translateY(-2px);
            border-color: rgba(255, 255, 255, 0.18);
            filter: brightness(1.06);
        }

        /* Active wie deine “active” Elemente */
        .veh-legend .row.active {
            border-color: rgba(255, 255, 255, 0.65);
            box-shadow:
                0 0 0 2px rgba(0, 0, 0, 0.65) inset,
                0 0 18px rgba(255, 255, 255, 0.12);
        }

        /* Punkt/Marker = “Badge” */
        .veh-legend .dot {
            width: 12px;
            height: 12px;
            border-radius: 3px;
            /* eckiger = mehr SCUM */
            border: 1px solid rgba(255, 255, 255, 0.18);
            box-shadow: 0 0 0 2px rgba(0, 0, 0, 0.55) inset;
            flex: 0 0 auto;
        }

        /* Optional: Count rechts ausrichten (wenn du später spans einbaust) */
        .veh-legend .row .cnt {
            margin-left: auto;
            opacity: .75;
            font-size: 11px;
            letter-spacing: 1px;
        }
    </style>


    <main class="content layout-3col">

        <!-- LEFT -->
        <aside class="side left">
            <section class="panel panel-left">
                <div class="panel-topbar">
                    <div class="panel-topbar-title">KARTENWERKZEUGE</div>
                </div>
                <div class="panel-section map-side-stack">
                    <a href="index.php?page=quests_overview" class="map-overview-link">
                        <span>Questübersicht</span><i aria-hidden="true">↗</i>
                    </a>
                    <div class="map-section-label">Quests nach Trader</div>
                    <input id="questSearch" class="input" placeholder="Suche…" style="display:none">
                    <select id="questNpc" class="input" style="display:none">
                        <option value="">Alle Trader</option>
                    </select>
                    <div id="questList" class="map-quest-list"></div>
                </div>
            </section>
        </aside>

        <section class="center">
            <div class="map-inner" id="mapInner">
                <img id="mapImage" class="map-image" src="assets/img/scum_map.jpg" alt="SCUM Map" />
                <canvas id="zoneLayer" class="map-overlay"></canvas>
                <canvas id="questLayer" class="map-overlay"></canvas>
                <canvas id="vehicleLayer" class="map-overlay"></canvas>



            </div>
        </section>

        <aside class="side right">
            <section class="panel panel-right">
                <div class="panel-topbar">
                    <div class="panel-topbar-title">KARTENINFOS</div>
                </div>
                <div class="panel-section map-details-stack">
                    <section id="questDetail" class="map-info-card">
                        <span class="map-card-kicker">QUEST</span>
                        <h2>Details</h2>
                        <p class="muted">Bewege den Mauszeiger über eine Quest, um ihre Position und Details zu sehen.</p>
                    </section>

                    <section class="map-info-card" id="vehPanel">
                        <div class="map-info-card__head">
                            <div><span class="map-card-kicker">SERVERKARTE</span><h2>Fahrzeuge</h2></div>
                            <span class="map-access-state <?= $VEH_ACCESS_GRANTED ? 'is-online' : 'is-offline' ?>"><i></i><?= $VEH_ACCESS_GRANTED ? 'AKTIV' : 'GESPERRT' ?></span>
                        </div>

                        <?php if (!$VEH_ACCESS_GRANTED): ?>
                            <p class="map-required-item">Benötigtes Item: <strong><?= h($requiredItemName) ?></strong></p>
                        <?php endif; ?>

                        <div class="map-switch-list">
                            <label class="map-toggle">
                                <span>Freie Fahrzeuge</span>
                                <input type="checkbox" id="vehToggleRight" checked>
                            </label>
                            <label class="map-toggle">
                                <span>Dein Fahrzeug</span>
                                <input type="checkbox" id="vehOwnerSelf" checked>
                            </label>
                            <label class="map-toggle">
                                <span>Squad-Fahrzeuge</span>
                                <input type="checkbox" id="vehOwnerSquad" checked>
                            </label>
                        </div>

                        <div id="vehSquadHint" class="map-inline-note" style="display:none;">
                            Du bist in keinem Squad oder es wurden keine Squad-Fahrzeuge gefunden.
                        </div>
                        <div id="vehLegendRight" class="veh-legend"></div>
                    </section>
                </div>
            </section>
        </aside>
    </main>

        <div id="zone-tooltip"></div>

        <script>
            const configs = <?= json_encode($configs, JSON_UNESCAPED_UNICODE) ?>;
            const regions = <?= json_encode($regions, JSON_UNESCAPED_UNICODE) ?>;

            // SCUM-Koordinaten (wie bei dir)
            const worldLeftX = 618000;
            const worldRightX = -898000;
            const worldTopY = 618000;
            const worldBottomY = -900000;

            const worldSpanX = worldLeftX - worldRightX; // > 0
            const worldSpanY = worldTopY - worldBottomY; // > 0
            const openNpc = new Set(); // merkt, welche Trader offen sind
            const mapImage = document.getElementById('mapImage');
            const canvas = document.getElementById('zoneLayer');
            const ctx = canvas.getContext('2d');
            const mapInner = document.getElementById('mapInner');
            const questDetailEl = document.getElementById('questDetail');
            const vehToggleRightEl = document.getElementById('vehToggleRight');
            const vehLegendRightEl = document.getElementById('vehLegendRight');
            const VEH_FREE_COUNTS = <?= json_encode($vehicleFreeCounts, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) ?>;
            const VEH_FREE_TOTAL = <?= (int)$vehicleFreeTotal ?>;

            // Zoom + Pan
            let zoomLevel = 1;
            const minZoom = 1;
            const maxZoom = 5.0;
            const zoomStep = 0.15;

            const VEH_ACCESS_GRANTED = <?= $VEH_ACCESS_GRANTED ? 'true' : 'false' ?>;
            const VEHICLES = <?= json_encode($vehiclesForClient, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) ?>;
            const VEH_COUNTS = <?= json_encode($vehicleCountsForClient, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) ?>;
            const VEH_TOTAL = <?= (int)$vehicleTotalForClient ?>;
            const SQUAD_HAS = <?= $squadHas ? 'true' : 'false' ?>;

            const VEH_CLASS_COLORS = <?= json_encode($VEHICLE_CLASS_COLORS, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) ?>;


            let panX = 0;
            let panY = 0;

            let isDragging = false;
            let dragStartX = 0;
            let dragStartY = 0;
            let startPanX = 0;
            let startPanY = 0;
            const QUESTS = <?= json_encode($quests, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) ?>;

            let hoverQuestId = null;
            const questCanvas = document.getElementById('questLayer');
            const qctx = questCanvas.getContext('2d');

            let drawnZones = [];
            const vehicleCanvas = document.getElementById('vehicleLayer');
            const vctx = vehicleCanvas.getContext('2d');
            const vehLegendEl = document.getElementById('vehLegend');
            const vehToggleEl = document.getElementById('vehToggle');

            let showFree = true; // Checkbox steuert nur freie Fahrzeuge
            let activeVehClass = null;
            let vehMarkers = []; // für Tooltip Hit-Test
            const vehOwnerSelfEl = document.getElementById('vehOwnerSelf');
            const vehOwnerSquadEl = document.getElementById('vehOwnerSquad');
            const vehSquadHintEl = document.getElementById('vehSquadHint');

            let showSelf = true;
            let showSquad = true;
            // Free nur wenn Zugriff
            if (!VEH_ACCESS_GRANTED) {
                showFree = false;
                if (vehToggleRightEl) {
                    vehToggleRightEl.checked = false;
                    vehToggleRightEl.disabled = true; // optional, aber sinnvoll
                }
            }
            if (!SQUAD_HAS) {
                // Squad-Filter deaktivieren
                if (vehOwnerSquadEl) vehOwnerSquadEl.checked = false;
                if (vehOwnerSquadEl) vehOwnerSquadEl.disabled = true;
                showSquad = false;
                if (vehSquadHintEl) vehSquadHintEl.style.display = 'block';
            }

            vehOwnerSelfEl?.addEventListener('change', () => {
                showSelf = !!vehOwnerSelfEl.checked;
                drawVehicleMarkers();
            });
            vehOwnerSquadEl?.addEventListener('change', () => {
                showSquad = !!vehOwnerSquadEl.checked;
                drawVehicleMarkers();
            });

            function worldToPixelX(x, mapWidth) {
                return ((x - worldLeftX) / (worldRightX - worldLeftX)) * mapWidth;
            }

            function worldToPixelY(y, mapHeight) {
                return ((y - worldTopY) / (worldBottomY - worldTopY)) * mapHeight;
            }

            function darkerStrokeColor(conf) {
                if (!conf) return 'rgba(0,0,0,0.9)';
                const factor = 0.55;
                const r = Math.max(0, Math.min(255, Math.round(conf.r * factor)));
                const g = Math.max(0, Math.min(255, Math.round(conf.g * factor)));
                const b = Math.max(0, Math.min(255, Math.round(conf.b * factor)));
                return `rgba(${r},${g},${b},0.9)`;
            }

            function clampPan() {
                const viewportWidth = mapInner.clientWidth;
                const viewportHeight = mapInner.clientHeight;

                const scaledWidth = viewportWidth * zoomLevel;
                const scaledHeight = viewportHeight * zoomLevel;

                const maxOffsetX = Math.max(0, (scaledWidth - viewportWidth) / 2);
                const maxOffsetY = Math.max(0, (scaledHeight - viewportHeight) / 2);

                if (scaledWidth <= viewportWidth) panX = 0;
                else panX = Math.max(-maxOffsetX, Math.min(maxOffsetX, panX));

                if (scaledHeight <= viewportHeight) panY = 0;
                else panY = Math.max(-maxOffsetY, Math.min(maxOffsetY, panY));
            }

            function applyTransform() {
                clampPan();
                const transform = `translate(${panX}px, ${panY}px) scale(${zoomLevel})`;

                mapImage.style.transform = transform;
                canvas.style.transform = transform;
                questCanvas.style.transform = transform;

                mapImage.style.transformOrigin = 'center center';
                canvas.style.transformOrigin = 'center center';
                questCanvas.style.transformOrigin = 'center center';

                mapInner.style.cursor = isDragging ? 'grabbing' : 'grab';
                vehicleCanvas.style.transform = transform;
                vehicleCanvas.style.transformOrigin = 'center center';

            }

            function resizeQuestLayerToMap() {}

            function drawQuestMarkers() {
                const w = questCanvas.width,
                    h = questCanvas.height;
                qctx.clearRect(0, 0, w, h);

                if (!hoverQuestId) return;

                const q = QUESTS.find(x => x.id === hoverQuestId);
                if (!q || !Array.isArray(q.points) || q.points.length === 0) return;

                qctx.save();
                qctx.lineWidth = 2;

                q.points.forEach((p, idx) => {
                    const px = worldToPixelX(p.x, w);
                    const py = worldToPixelY(p.y, h);

                    const r = 7;
                    qctx.beginPath();
                    qctx.arc(px, py, r, 0, Math.PI * 2);
                    qctx.fillStyle = 'rgba(34,197,94,0.95)';
                    qctx.fill();
                    qctx.strokeStyle = 'rgba(255,255,255,0.95)';
                    qctx.stroke();

                    const label = (p.seq != null) ? String(p.seq) : String(idx + 1);
                    qctx.font = '12px system-ui, -apple-system, Segoe UI, Roboto, sans-serif';
                    qctx.textAlign = 'center';
                    qctx.textBaseline = 'middle';
                    qctx.fillStyle = 'rgba(0,0,0,0.9)';
                    qctx.fillText(label, px, py);
                });

                qctx.restore();
            }

            function resizeAllLayers() {
                const w = mapInner.clientWidth;
                const h = mapInner.clientHeight;

                // Wichtig: die "echte" Canvas-Auflösung (nicht CSS) setzen
                canvas.width = w;
                canvas.height = h;

                questCanvas.width = w;
                questCanvas.height = h;

                vehicleCanvas.width = w;
                vehicleCanvas.height = h;
            }

            function drawZones() {
                const mapWidth = canvas.width;
                const mapHeight = canvas.height;

                ctx.clearRect(0, 0, mapWidth, mapHeight);
                drawnZones = [];

                regions.forEach(zone => {
                    const centerX = worldToPixelX(zone.x, mapWidth);
                    const centerY = worldToPixelY(zone.y, mapHeight);

                    const conf = configs[zone.config_id];
                    ctx.fillStyle = conf ? conf.fill : 'rgba(0,255,0,0.3)';
                    ctx.strokeStyle = darkerStrokeColor(conf);
                    ctx.lineWidth = 1.5;

                    // Kreis wenn size_y ~ 0
                    const isCircle = Math.abs(zone.size_y) < 0.001;

                    if (isCircle) {
                        const radiusWorld = zone.size_x;
                        const radiusPx = (radiusWorld / worldSpanX) * mapWidth;

                        ctx.beginPath();
                        ctx.arc(centerX, centerY, radiusPx, 0, Math.PI * 2);
                        ctx.fill();
                        ctx.stroke();

                        drawnZones.push({
                            type: 'circle',
                            zone,
                            cx: centerX,
                            cy: centerY,
                            r: radiusPx
                        });
                    } else {
                        const w = (zone.size_x / worldSpanX) * mapWidth;
                        const h = (zone.size_y / worldSpanY) * mapHeight;

                        const x = centerX - w / 2;
                        const y = centerY - h / 2;

                        ctx.beginPath();
                        ctx.rect(x, y, w, h);
                        ctx.fill();
                        ctx.stroke();

                        drawnZones.push({
                            type: 'rect',
                            zone,
                            x,
                            y,
                            w,
                            h
                        });
                    }
                });
            }

            // Tooltip + Hit-Test
            const tooltip = document.getElementById('zone-tooltip');

            function showTooltip(text, clientX, clientY) {
                tooltip.textContent = text;
                tooltip.style.left = (clientX + 12) + 'px';
                tooltip.style.top = (clientY + 12) + 'px';
                tooltip.style.display = 'block';
            }

            function hideTooltip() {
                tooltip.style.display = 'none';
            }

            function findZoneAtPixel(px, py) {
                for (let i = drawnZones.length - 1; i >= 0; i--) {
                    const dz = drawnZones[i];
                    if (dz.type === 'circle') {
                        const dx = px - dz.cx;
                        const dy = py - dz.cy;
                        if (dx * dx + dy * dy <= dz.r * dz.r) return dz.zone;
                    } else {
                        if (px >= dz.x && px <= dz.x + dz.w && py >= dz.y && py <= dz.y + dz.h) return dz.zone;
                    }
                }
                return null;
            }

            function initMap() {
                const onReady = () => {
                    resizeAllLayers();
                    drawZones();
                    drawQuestMarkers();
                    applyTransform();
                    drawVehicleMarkers();
                };

                if (!mapImage.complete) mapImage.addEventListener('load', onReady, {
                    once: true
                });
                else onReady();

                window.addEventListener('resize', () => {
                    resizeAllLayers();
                    drawZones();
                    drawQuestMarkers();
                    applyTransform();
                    drawVehicleMarkers();
                });

                // Hover Tooltip (mit Canvas-Scaling korrekt)
                canvas.addEventListener('mousemove', (e) => {
                    const rect = canvas.getBoundingClientRect();
                    const scaleX = canvas.width / rect.width;
                    const scaleY = canvas.height / rect.height;
                    const px = (e.clientX - rect.left) * scaleX;
                    const py = (e.clientY - rect.top) * scaleY;

                    const zone = findZoneAtPixel(px, py);
                    if (zone) {
                        const name = (zone.name && zone.name.trim().length) ? zone.name : `Zone #${zone.id}`;
                        showTooltip(name, e.clientX, e.clientY);
                    } else {
                        hideTooltip();
                    }
                });
                questCanvas.addEventListener('mousemove', (e) => {
                    if (!hoverQuestId) return;

                    const rect = questCanvas.getBoundingClientRect();
                    const scaleX = questCanvas.width / rect.width;
                    const scaleY = questCanvas.height / rect.height;
                    const px = (e.clientX - rect.left) * scaleX;
                    const py = (e.clientY - rect.top) * scaleY;

                    // simple hit test: check distance to markers
                    const q = QUESTS.find(x => x.id === hoverQuestId);
                    if (!q?.points?.length) return hideTooltip();

                    for (let i = q.points.length - 1; i >= 0; i--) {
                        const p = q.points[i];
                        const mx = worldToPixelX(p.x, questCanvas.width);
                        const my = worldToPixelY(p.y, questCanvas.height);
                        const dx = px - mx,
                            dy = py - my;
                        if (dx * dx + dy * dy <= 12 * 12) { // 12px hit radius
                            const label = (p.seq != null) ? p.seq : (i + 1);
                            showTooltip(`${q.title} · Step ${label}${p.cap ? ' · ' + p.cap : ''}`, e.clientX, e.clientY);
                            return;
                        }
                    }
                    hideTooltip();
                });

                canvas.addEventListener('mouseleave', hideTooltip);

                // Zoom Buttons
                document.getElementById('zoomInBtn')?.addEventListener('click', () => {
                    zoomLevel = Math.min(maxZoom, zoomLevel + zoomStep);
                    applyTransform();
                });
                document.getElementById('zoomOutBtn')?.addEventListener('click', () => {
                    zoomLevel = Math.max(minZoom, zoomLevel - zoomStep);
                    applyTransform();
                });

                // Wheel Zoom
                mapInner.addEventListener('wheel', (e) => {
                    e.preventDefault();
                    const delta = e.deltaY > 0 ? -zoomStep : zoomStep;
                    zoomLevel = Math.max(minZoom, Math.min(maxZoom, zoomLevel + delta));
                    applyTransform();
                }, {
                    passive: false
                });

                // Drag / Pan
                mapInner.style.cursor = 'grab';

                mapInner.addEventListener('mousedown', (e) => {
                    e.preventDefault();
                    isDragging = true;
                    dragStartX = e.clientX;
                    dragStartY = e.clientY;
                    startPanX = panX;
                    startPanY = panY;
                    applyTransform();
                });

                window.addEventListener('mousemove', (e) => {
                    if (!isDragging) return;
                    panX = startPanX + (e.clientX - dragStartX);
                    panY = startPanY + (e.clientY - dragStartY);
                    applyTransform();
                });

                window.addEventListener('mouseup', () => {
                    if (!isDragging) return;
                    isDragging = false;
                    applyTransform();
                });
                vehToggleEl?.addEventListener('change', () => {
                    showFree = !!vehToggleEl.checked;
                    if (!showFree) activeVehClass = null;
                    drawVehicleMarkers();
                });

                // Tooltip Hit-Test Vehicles
                vehicleCanvas.addEventListener('mousemove', (e) => {
                    if (!vehMarkers.length) return; // Zugriff entscheidet, nicht showFree

                    const rect = vehicleCanvas.getBoundingClientRect();
                    const scaleX = vehicleCanvas.width / rect.width;
                    const scaleY = vehicleCanvas.height / rect.height;
                    const px = (e.clientX - rect.left) * scaleX;
                    const py = (e.clientY - rect.top) * scaleY;

                    for (let i = vehMarkers.length - 1; i >= 0; i--) {
                        const m = vehMarkers[i];
                        const dx = px - m.px,
                            dy = py - m.py;
                        if (dx * dx + dy * dy <= m.r * m.r) {
                            const ownerTag =
                                (m.owner === 'self') ? 'Du' :
                                (m.owner === 'squad') ? (m.ownerName || 'Squadmate') :
                                'Frei';

                            const lockTag = m.locked ? '🔒' : '🔓';
                            showTooltip(`${lockTag} ${m.cls} #${m.id} · ${ownerTag}`, e.clientX, e.clientY);
                            return;
                        }
                    }
                    // nur verstecken, wenn nicht gerade Zone/Quest was zeigt
                    hideTooltip();
                });
                vehicleCanvas.addEventListener('mouseleave', hideTooltip);

            }

            const questListEl = document.getElementById('questList');
            const questNpcEl = document.getElementById('questNpc');
            const questSearchEl = document.getElementById('questSearch');

            function esc(s) {
                return String(s ?? '').replace(/[&<>"']/g, m => ({
                    '&': '&amp;',
                    '<': '&lt;',
                    '>': '&gt;',
                    '"': '&quot;',
                    "'": '&#39;'
                } [m]));
            }

            function renderQuestDetail(q) {
                if (!questDetailEl) return;
                if (!q) {
                    questDetailEl.innerHTML = `
      <div class="section-title">Details</div>
      <div class="muted">Hover über eine Quest…</div>
    `;
                    return;
                }

                const pointsCount = Array.isArray(q.points) ? q.points.length : 0;
                const updated = q.mtime ? new Date(q.mtime * 1000).toLocaleString('de-DE') : '—';

                // kleine Liste der Steps (nur Interaction-Punkte)
                const stepsHtml = pointsCount ? `
    <div style="margin-top:10px">
      <div class="section-title">Zielpunkte</div>
      <div style="display:flex;flex-direction:column;gap:6px">
        ${q.points.slice(0, 12).map((p, idx) => {
          const label = (p.seq != null) ? p.seq : (idx + 1);
          const cap = p.cap ? ` · ${esc(p.cap)}` : '';
          return `<div class="muted" style="font-size:12px">Step ${label}${cap}</div>`;
        }).join('')}
        ${pointsCount > 12 ? `<div class="muted" style="font-size:12px">+ ${pointsCount - 12} weitere…</div>` : ``}
      </div>
    </div>
  ` : `<div class="muted" style="margin-top:10px">Keine Interaction-Zielpunkte gefunden.</div>`;

                questDetailEl.innerHTML = `
    <div class="section-title">Details</div>
    <div style="font-weight:800">${esc(q.title)}</div>
    <div class="muted" style="font-size:12px;margin-top:4px">
      Trader: ${esc(q.npc || 'Unknown')} · Tier ${q.tier|0} · Datei: ${esc(q.file)} · Update: ${esc(updated)}
    </div>
    ${q.desc ? `<div style="margin-top:10px">${esc(q.desc)}</div>` : `<div class="muted" style="margin-top:10px">Keine Beschreibung.</div>`}
    ${stepsHtml}
  `;
            }

            function buildNpcOptions() {
                const npcs = Array.from(new Set(QUESTS.map(q => q.npc || 'Unknown')))
                    .sort((a, b) => a.localeCompare(b, undefined, {
                        sensitivity: 'base'
                    }));
                questNpcEl.innerHTML = `<option value="">Alle Trader</option>` + npcs.map(n => `<option value="${esc(n)}">${esc(n)}</option>`).join('');
            }

            function textOf(v) {
                if (v == null) return '';
                if (typeof v === 'string') return v;
                try {
                    return JSON.stringify(v);
                } catch {
                    return String(v);
                }
            }

            function filteredQuests() {
                const npc = (questNpcEl.value || '').trim();
                const q = (questSearchEl.value || '').trim().toLowerCase();

                return QUESTS.filter(it => {
                    const itNpc = String(it.npc ?? it.NPC ?? it.Trader ?? it.AssociatedNPC ?? 'Unknown');
                    const title = String(it.title ?? it.Title ?? it.name ?? it.Name ?? it.file ?? '');
                    const desc = String(it.desc ?? it.Description ?? it.description ?? '');
                    const file = String(it.file ?? it.filename ?? '');
                    const conds = textOf(it.conds ?? it.Conditions ?? it.conditions ?? '');
                    const rewards = textOf(it.rewards ?? it.RewardPool ?? it.rewardPool ?? '');

                    if (npc && itNpc !== npc) return false;

                    if (!q) return true;

                    const hay = (title + ' ' + desc + ' ' + file + ' ' + conds + ' ' + rewards).toLowerCase();
                    return hay.includes(q);
                });
            }


            function groupByNpc(list) {
                const buckets = {};
                list.forEach(q => (buckets[q.npc || 'Unknown'] ||= []).push(q));
                Object.keys(buckets).forEach(k => buckets[k].sort((a, b) => (a.tier - b.tier) || String(a.title).localeCompare(String(b.title))));
                return Object.entries(buckets).sort((a, b) => a[0].localeCompare(b[0], undefined, {
                    sensitivity: 'base'
                }));
            }

            function renderQuestList() {
                const list = filteredQuests();
                const grouped = groupByNpc(list);
                if ((questSearchEl.value || '').trim()) {
                    grouped.forEach(([npc]) => openNpc.add(npc));
                }
                if (!grouped.length) {
                    questListEl.innerHTML = `<div class="muted">Keine Quests gefunden.</div>`;
                    return;
                }

                questListEl.innerHTML = grouped.map(([npc, arr]) => {
                    const isOpen = openNpc.has(npc); // state
                    return `
      <details class="quest-group" data-npc="${esc(npc)}" ${isOpen ? 'open' : ''} style="margin-bottom:10px">
        <summary style="cursor:pointer; user-select:none; display:flex; align-items:center; justify-content:space-between; gap:8px">
          <span class="section-title" style="margin:8px 0 6px 0">${esc(npc)}</span>
          <span class="badge">${arr.length}</span>
        </summary>

        <div class="quest-items" style="display:flex;flex-direction:column;gap:8px; padding-top:6px">
          ${arr.map(q => `
            <div class="quest-item ${String(q.id)===String(hoverQuestId)?'active':''}"
                 data-qid="${esc(q.id)}"
                 style="border:1px solid rgba(148,163,184,.18);border-radius:12px;padding:8px 10px;cursor:pointer">
              <div style="font-weight:700">${esc(q.title ?? q.Title ?? q.file ?? 'Quest')} <span class="badge">Tier ${(q.tier ?? q.Tier ?? 1)|0}</span></div>
              <div class="muted" style="font-size:12px;margin-top:2px">${esc(q.file ?? '')}${(q.points?.length)?` · Points: ${q.points.length}`:''}</div>
            </div>
          `).join('')}
        </div>
      </details>
    `;
                }).join('');

                // Merke Open/Close State
                questListEl.querySelectorAll('details.quest-group').forEach(d => {
                    d.addEventListener('toggle', () => {
                        const npc = d.getAttribute('data-npc') || '';
                        if (!npc) return;
                        if (d.open) openNpc.add(npc);
                        else openNpc.delete(npc);
                    }, {
                        passive: true
                    });
                });

                // Hover auf Questitems (Marker + Detail)
                questListEl.querySelectorAll('.quest-item[data-qid]').forEach(el => {
                    el.addEventListener('mouseenter', () => {
                        hoverQuestId = el.getAttribute('data-qid');
                        const q = QUESTS.find(x => String(x.id) === String(hoverQuestId));
                        renderQuestDetail(q);
                        drawQuestMarkers();

                        questListEl.querySelectorAll('.quest-item.active').forEach(x => x.classList.remove('active'));
                        el.classList.add('active');
                    });

                    el.addEventListener('mouseleave', () => {
                        if (hoverQuestId === el.getAttribute('data-qid')) {
                            hoverQuestId = null;
                            renderQuestDetail(null);
                            drawQuestMarkers();
                            el.classList.remove('active');
                        }
                    });
                });
            }

            const VEH_FALLBACK = ['#e41a1c', '#377eb8', '#4daf4a', '#984ea3', '#ff7f00', '#ffff33', '#a65628', '#f781bf', '#999999'];
            const vehColorCache = {};

            function vehColorForClass(cls) {
                cls = cls || 'Unknown';
                if (VEH_CLASS_COLORS[cls]) return VEH_CLASS_COLORS[cls];
                if (!vehColorCache[cls]) {
                    let h = 0;
                    for (let i = 0; i < cls.length; i++) h = (h * 31 + cls.charCodeAt(i)) >>> 0;
                    vehColorCache[cls] = VEH_FALLBACK[h % VEH_FALLBACK.length];
                }
                return vehColorCache[cls];
            }

            function renderRightVehicleLegendAlways() {
                if (!vehLegendRightEl) return;

                // Legend rechts soll IMMER angezeigt werden (Counts), unabhängig von Access/Toggle
                const counts = VEH_FREE_COUNTS || {};
                const total = VEH_FREE_TOTAL || 0;

                vehLegendRightEl.style.display = 'block';
                vehLegendRightEl.innerHTML = '';

                // Wenn DB nix liefert
                if (!total || Object.keys(counts).length === 0) {
                    vehLegendRightEl.innerHTML = `<div class="muted" style="font-size:12px;">Keine freien Fahrzeuge gefunden.</div>`;
                    return;
                }

                // Hinweis (wenn kein Zugriff -> nur Counts)
                if (!VEH_ACCESS_GRANTED) {
                    const note = document.createElement('div');
                    note.className = 'muted';
                    note.style.cssText = 'font-size:12px;margin-bottom:8px;opacity:.85;';
                    note.textContent = 'Nur freie Anzahl (ohne Zugriff keine Positionen).';
                    vehLegendRightEl.appendChild(note);
                }

                // Row helper
                const addRow = (label, color) => {
                    const row = document.createElement('div');
                    row.className = 'row';
                    row.innerHTML = `<span class="dot" style="background:${color}"></span> ${label}`;
                    row.style.cursor = 'default'; // rechts = nur Anzeige
                    vehLegendRightEl.appendChild(row);
                };

                addRow(`Alle (${total})`, '#bbb');

                Object.entries(counts)
                    .sort((a, b) => b[1] - a[1])
                    .forEach(([cls, cnt]) => {
                        addRow(`${cls} (${cnt})`, vehColorForClass(cls));
                    });
            }

            function resizeVehicleLayerToMap() {}

            function buildVehicleLegend(counts, total) {
                const targets = [vehLegendEl].filter(Boolean);

                targets.forEach(t => {
                    if (!VEH_ACCESS_GRANTED || !VEHICLES.length) {
                        t.style.display = 'none';
                        t.innerHTML = '';
                        return;
                    }
                    t.style.display = showFree ? 'block' : 'none';
                    t.innerHTML = '';

                    const mkRow = (cls, label, color, active) => {
                        const row = document.createElement('div');
                        row.className = 'row' + (active ? ' active' : '');
                        row.dataset.cls = cls;
                        row.innerHTML = `<span class="dot" style="background:${color}"></span> ${label}`;
                        row.addEventListener('click', () => {
                            activeVehClass = (activeVehClass === cls || cls === '__ALL__') ? null : cls;
                            drawVehicleMarkers();
                        });
                        t.appendChild(row);
                    };

                    mkRow('__ALL__', `Alle (${total})`, '#bbb', !activeVehClass);
                    Object.entries(counts).sort((a, b) => b[1] - a[1]).forEach(([cls, cnt]) => {
                        mkRow(cls, `${cls} (${cnt})`, vehColorForClass(cls), activeVehClass === cls);
                    });
                });
            }


            function drawVehicleMarkers() {
                const w = vehicleCanvas.width,
                    h = vehicleCanvas.height;
                const counts = {};
                let total = 0;
                vctx.clearRect(0, 0, w, h);
                vehMarkers = [];

                if (!VEHICLES.length) return;

                // counts für Legend
                VEHICLES.forEach(v => {
                    // Freie Fahrzeuge per Checkbox
                    if (v.owner === 'free' && !showFree) return;

                    // Owned Filter bleiben wie gehabt
                    if (v.owner === 'self' && !showSelf) return;
                    if (v.owner === 'squad' && !showSquad) return;

                    counts[v.class] = (counts[v.class] || 0) + 1;
                    total++;
                });


                buildVehicleLegend(counts, total);
                vctx.save();
                vctx.lineWidth = 1.5;

                VEHICLES.forEach(v => {
                    const cls = v.class || 'Unknown';
                    const px = worldToPixelX(v.x, w);
                    const py = worldToPixelY(v.y, h);

                    // Highlight: andere Klassen "ausgrauen"
                    const a = activeVehClass && activeVehClass !== cls ? 0.20 : 0.95;

                    const r = 6;
                    if (v.owner === 'free' && !showFree) return;
                    // Owner Filter
                    if (v.owner === 'self' && !showSelf) return;
                    if (v.owner === 'squad' && !showSquad) return;
                    vctx.globalAlpha = a;
                    vctx.beginPath();
                    vctx.arc(px, py, r, 0, Math.PI * 2);
                    vctx.fillStyle = vehColorForClass(cls);
                    vctx.fill();
                    vctx.strokeStyle = 'rgba(0,0,0,0.55)';
                    vctx.stroke();

                    vehMarkers.push({
                        px,
                        py,
                        r: 10,
                        id: v.id,
                        cls,
                        owner: v.owner,
                        ownerName: v.ownerName,
                        locked: !!v.locked,
                        x: v.x,
                        y: v.y
                    });
                });

                vctx.restore();
                vctx.globalAlpha = 1;
            }

            function setShowFree(v) {
                showFree = !!v;
                if (vehToggleRightEl) vehToggleRightEl.checked = showFree;
                drawVehicleMarkers();
            }



            vehToggleEl?.addEventListener('change', () => setShowFree(vehToggleEl.checked));
            vehToggleRightEl?.addEventListener('change', () => setShowFree(vehToggleRightEl.checked));




            initMap();
            buildNpcOptions();
            renderQuestList();
            drawVehicleMarkers();
            renderRightVehicleLegendAlways();
        </script>
