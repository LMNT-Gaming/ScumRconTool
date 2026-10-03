<?php
// /functions/base_stock_function.php
declare(strict_types=1);

require_once __DIR__ . '/scum_db.php';

/**
 * Konstante Filterliste: welche base_element.asset als "Container" zählen.
 * -> Du passt das später an (wardrobe, cabinets, chests, etc.)
 */
function base_stock_asset_like_filters(): array
{
    return [
        '%wardrobe%',
        '%BP_Cabinet_Large%',
        '%Drawer%',
        '%BP_Chest_%',
    ];
}


/**
 * Hilfsfunktion: _name aus NameableComponent im XML ziehen.
 * Erwartet z.B. <Component Name="NameableComponent" _name="ABC" ... />
 */
function base_stock_extract_nameable(?string $xml): string
{
    if (!$xml) return '';

    // Robust: erst prüfen ob NameableComponent vorkommt, dann _name holen
    if (stripos($xml, 'NameableComponent') === false) return '';

    if (preg_match('/NameableComponent[^>]*\s_name="([^"]+)"/i', $xml, $m)) {
        return trim($m[1]);
    }
    if (preg_match('/<Component[^>]*Name="NameableComponent"[^>]*_name="([^"]+)"/i', $xml, $m)) {
        return trim($m[1]);
    }
    return '';
}

/**
 * Hauptfunktion: liefert Container + Items für die Squad-Basis.
 *
 * Return:
 * [
 *   [
 *     'element_id' => int,
 *     'asset' => string,
 *     'owner_user_profile_id' => int,
 *     'container_entity_id' => int, // base_element_item.item_entity_id
 *     'container_name' => string,   // aus item_entity.xml
 *     'items' => [ ['class' => '...', 'count' => 1], ... ] // optional bereits gruppiert
 *   ],
 *   ...
 * ]
 */
function base_stock_load_for_steamid(string $steamId): array
{
    $db = getScumDb(); // SQLite3 (read-only)

    // 1) user_profile.id (UserProfileId) anhand SteamID (user_profile.user_id) holen
    $st = $db->prepare('SELECT id FROM user_profile WHERE user_id = :sid LIMIT 1');
    $st->bindValue(':sid', $steamId, SQLITE3_TEXT);
    $r = $st->execute();
    $row = $r ? $r->fetchArray(SQLITE3_ASSOC) : null;
    $r?->finalize();
    $st->close();

    $myProfileId = (int)($row['id'] ?? 0);
    if ($myProfileId <= 0) return [];

    // 2) squad_id für diesen Spieler holen
    $st = $db->prepare('SELECT squad_id FROM squad_member WHERE user_profile_id = :upid LIMIT 1');
    $st->bindValue(':upid', $myProfileId, SQLITE3_INTEGER);
    $r = $st->execute();
    $row = $r ? $r->fetchArray(SQLITE3_ASSOC) : null;
    $r?->finalize();
    $st->close();

    $squadId = (int)($row['squad_id'] ?? 0);
    if ($squadId <= 0) return []; // kein Squad

    // 3) alle user_profile_id der SquadMembers laden
    $members = [];
    $st = $db->prepare('SELECT user_profile_id FROM squad_member WHERE squad_id = :sid');
    $st->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $r = $st->execute();
    while ($r && ($m = $r->fetchArray(SQLITE3_ASSOC))) {
        $members[] = (int)$m['user_profile_id'];
    }
    $r?->finalize();
    $st->close();

    if (!$members) return [];

    // 4) Container-BaseElements laden (owner_user_profile_id IN members) + Asset Filter
    $filters = base_stock_asset_like_filters();

    // IN (...) placeholders
    $inPlaceholders = implode(',', array_fill(0, count($members), '?'));

    // Asset LIKE (...) OR (...) placeholders
    $likeWhere = implode(' OR ', array_fill(0, count($filters), 'be.asset LIKE ?'));

    /**
     * Wir holen hier direkt:
     * - base_element (Container)
     * - base_element_item (verknüpft element_id -> item_entity_id)
     * - item_entity.xml (für Nameable)
     *
     * Hinweis: base_element_item kann ggf. mehrere Zeilen liefern,
     * aber üblicherweise ist "item_entity_id" der Container-Entity (Parent).
     */
    $sqlContainers = "
        SELECT
            be.element_id,
            be.asset,
            be.owner_profile_id,
            bei.item_entity_id AS container_entity_id,
            ie.xml AS container_xml
        FROM base_element be
        JOIN base_element_item bei ON bei.element_id = be.element_id
        LEFT JOIN item_entity ie ON ie.entity_id = bei.item_entity_id
        WHERE be.owner_profile_id IN ($inPlaceholders)
          AND ($likeWhere)
        ORDER BY be.element_id DESC
    ";

    $st = $db->prepare($sqlContainers);

    // Bind members (IN)
    $bindIndex = 1;
    foreach ($members as $upid) {
        $st->bindValue($bindIndex, $upid, SQLITE3_INTEGER);
        $bindIndex++;
    }
    // Bind LIKE filters
    foreach ($filters as $pat) {
        $st->bindValue($bindIndex, $pat, SQLITE3_TEXT);
        $bindIndex++;
    }

    $containers = [];
    $r = $st->execute();
    while ($r && ($c = $r->fetchArray(SQLITE3_ASSOC))) {
        $containerEntityId = (int)($c['container_entity_id'] ?? 0);
        $xml = (string)($c['container_xml'] ?? '');
        $name = base_stock_extract_nameable($xml);

        $containers[] = [
            'element_id' => (int)$c['element_id'],
            'asset' => (string)$c['asset'],
            'owner_profile_id' => (int)$c['owner_profile_id'],
            'container_entity_id' => $containerEntityId,
            'container_name' => $name, // kann leer sein
            'items' => [],
        ];
    }
    $r?->finalize();
    $st->close();

    if (!$containers) return [];

    // 5) Items pro Container laden:
    // base_element_item.item_entity_id = entity.parent_entity_id
    // -> entity.class sind die Items
    // Wir holen alle container_entity_id und laden die Items in einem Rutsch.
    $containerIds = array_values(array_unique(array_filter(array_map(
        fn($x) => (int)$x['container_entity_id'],
        $containers
    ))));

    if (!$containerIds) return $containers;

    $inC = implode(',', array_fill(0, count($containerIds), '?'));

    $sqlItems = "
        SELECT
            e.parent_entity_id AS container_entity_id,
            e.class AS item_class
        FROM entity e
        WHERE e.parent_entity_id IN ($inC)
          AND e.class IS NOT NULL
          AND e.class <> ''
    ";

    $st = $db->prepare($sqlItems);
    $i = 1;
    foreach ($containerIds as $cid) {
        $st->bindValue($i, $cid, SQLITE3_INTEGER);
        $i++;
    }

    $itemsByContainer = [];
    $r = $st->execute();
    while ($r && ($it = $r->fetchArray(SQLITE3_ASSOC))) {
        $cid = (int)$it['container_entity_id'];
        $cls = (string)$it['item_class'];
        if ($cls === '') continue;
        $itemsByContainer[$cid][] = $cls;
    }
    $r?->finalize();
    $st->close();

    // Optional: Items pro Container gruppieren (count)
    foreach ($containers as &$c) {
        $cid = (int)$c['container_entity_id'];
        $list = $itemsByContainer[$cid] ?? [];
        if (!$list) {
            $c['items'] = [];
            continue;
        }

        $counts = [];
        foreach ($list as $cls) {
            $counts[$cls] = ($counts[$cls] ?? 0) + 1;
        }

        $out = [];
        foreach ($counts as $cls => $cnt) {
            $out[] = ['class' => $cls, 'count' => $cnt];
        }

        // optional sort
        usort($out, fn($a,$b) => strcmp($a['class'], $b['class']));

        $c['items'] = $out;
    }
    unset($c);

    return $containers;
}

function base_stock_table_columns(SQLite3 $db, string $table): array
{
    $cols = [];
    $table = preg_replace('/[^a-zA-Z0-9_]/', '', $table);

    $res = $db->query("PRAGMA table_info($table)");
    if ($res) {
        while ($r = $res->fetchArray(SQLITE3_ASSOC)) {
            if (!empty($r['name'])) $cols[] = (string)$r['name'];
        }
        $res->finalize();
    }
    return $cols;
}

/**
 * Liefert Base-Location für den Squad des SteamIDs.
 * Rückgabe: ['id'=>..., 'x'=>..., 'y'=>..., 'squad_id'=>...] oder null
 */
function base_stock_get_base_location_for_steamid(string $steamId): ?array
{
    $db = getScumDb(); // SQLite3

    // 1) my profile id
    $st = $db->prepare('SELECT id FROM user_profile WHERE user_id = :sid LIMIT 1');
    $st->bindValue(':sid', $steamId, SQLITE3_TEXT);
    $r = $st->execute();
    $row = $r ? $r->fetchArray(SQLITE3_ASSOC) : null;
    $r?->finalize();
    $st->close();

    $myProfileId = (int)($row['id'] ?? 0);
    if ($myProfileId <= 0) return null;

    // 2) squad id
    $st = $db->prepare('SELECT squad_id FROM squad_member WHERE user_profile_id = :upid LIMIT 1');
    $st->bindValue(':upid', $myProfileId, SQLITE3_INTEGER);
    $r = $st->execute();
    $row = $r ? $r->fetchArray(SQLITE3_ASSOC) : null;
    $r?->finalize();
    $st->close();

    $squadId = (int)($row['squad_id'] ?? 0);
    if ($squadId <= 0) return null;

    // 3) base-location: bevorzugt via base.squad_id, sonst fallback via owner_profile_id IN members
    $cols = base_stock_table_columns($db, 'base');

    // --- bevorzugt: base.squad_id ---
    if (in_array('squad_id', $cols, true)) {
        $st = $db->prepare("
            SELECT id, location_x, location_y
            FROM base
            WHERE squad_id = :sid
            ORDER BY id DESC
            LIMIT 1
        ");
        $st->bindValue(':sid', $squadId, SQLITE3_INTEGER);

        $r = $st->execute();
        $row = $r ? $r->fetchArray(SQLITE3_ASSOC) : null;
        $r?->finalize();
        $st->close();

        if ($row) {
            $x = isset($row['location_x']) ? (float)$row['location_x'] : null;
            $y = isset($row['location_y']) ? (float)$row['location_y'] : null;
            if ($x !== null && $y !== null) {
                return [
                    'id' => (int)$row['id'],
                    'x'  => $x,
                    'y'  => $y,
                    'squad_id' => $squadId,
                ];
            }
        }
    }

    // --- fallback: members laden ---
    $members = [];
    $st = $db->prepare('SELECT user_profile_id FROM squad_member WHERE squad_id = :sid');
    $st->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $r = $st->execute();
    while ($r && ($m = $r->fetchArray(SQLITE3_ASSOC))) {
        $members[] = (int)$m['user_profile_id'];
    }
    $r?->finalize();
    $st->close();

    if (!$members) return null;

    $in = implode(',', array_fill(0, count($members), '?'));

    // welche Owner-Spalte existiert?
    if (in_array('owner_profile_id', $cols, true)) {
        $sql = "
            SELECT id, location_x, location_y
            FROM base
            WHERE owner_profile_id IN ($in)
            ORDER BY id DESC
            LIMIT 1
        ";
    } elseif (in_array('owner_user_profile_id', $cols, true)) {
        $sql = "
            SELECT id, location_x, location_y
            FROM base
            WHERE owner_user_profile_id IN ($in)
            ORDER BY id DESC
            LIMIT 1
        ";
    } else {
        // letzter Notnagel: einfach letzte base
        $sql = "
            SELECT id, location_x, location_y
            FROM base
            ORDER BY id DESC
            LIMIT 1
        ";
    }

    $st = $db->prepare($sql);

    // nur binden wenn IN(...) genutzt wird
    if (strpos($sql, 'IN (') !== false) {
        $i = 1;
        foreach ($members as $upid) {
            $st->bindValue($i, $upid, SQLITE3_INTEGER);
            $i++;
        }
    }

    $r = $st->execute();
    $row = $r ? $r->fetchArray(SQLITE3_ASSOC) : null;
    $r?->finalize();
    $st->close();

    if (!$row) return null;

    $x = isset($row['location_x']) ? (float)$row['location_x'] : null;
    $y = isset($row['location_y']) ? (float)$row['location_y'] : null;
    if ($x === null || $y === null) return null;

    return [
        'id' => (int)$row['id'],
        'x'  => $x,
        'y'  => $y,
        'squad_id' => $squadId,
    ];
}
