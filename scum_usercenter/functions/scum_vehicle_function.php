<?php
// /functions/scum_vehicle_function.php
declare(strict_types=1);

require_once __DIR__ . '/scum_db.php';

function scum_vehicle_extract_owner_upid(string $xml): ?int
{
    if ($xml === '') return null;

    // owningUserProfileId oder _owningUserProfileId
    if (preg_match('/\b_?owningUserProfileId\s*=\s*"(\d+)"/i', $xml, $m)) {
        $id = (int)$m[1];
        return $id > 0 ? $id : null;
    }
    return null;
}
function scum_admin_vehicle_type_counts(): array
{
    $db = getScumDbOrNull();
    if (!$db) return [];

    try {
        // Gesamtbestand: Anzahl pro vehicle_asset_id
        $res = $db->query("
            SELECT vehicle_asset_id, COUNT(*) AS cnt
            FROM vehicle_spawner
            GROUP BY vehicle_asset_id
            ORDER BY cnt DESC
        ");
        if (!$res) return [];

        $out = []; // vehicle_name => count

        while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
            $asset = (string)($row['vehicle_asset_id'] ?? '');
            $cnt   = (int)($row['cnt'] ?? 0);

            $name = str_replace("Vehicle:BPC_", "", $asset);
            if ($name === '' && $asset !== '') $name = $asset;
            if ($name === '') $name = 'Unknown';

            $out[$name] = ($out[$name] ?? 0) + $cnt;
        }

        // Sort: Name A-Z (case-insensitive)
        uksort($out, fn($a, $b) => strcasecmp($a, $b));

        return $out;
    } catch (Throwable $e) {
        scum_db_mark_syncing('copy_in_progress_or_malformed');
        return [];
    }
}

function scum_get_user_profile_names_by_ids(array $userProfileIds): array
{
    $ids = array_values(array_unique(array_filter(array_map('intval', $userProfileIds))));
    if (!$ids) return [];

    $db = getScumDb();

    $placeholders = implode(',', array_fill(0, count($ids), '?'));
    $stmt = $db->prepare("
        SELECT id, name
        FROM user_profile
        WHERE id IN ($placeholders)
    ");

    foreach ($ids as $i => $id) {
        $stmt->bindValue($i + 1, $id, SQLITE3_INTEGER);
    }

    $res = $stmt->execute();
    if (!$res) return [];

    $map = [];
    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $id = (int)($row['id'] ?? 0);
        $name = (string)($row['name'] ?? '');
        if ($id > 0) $map[$id] = $name !== '' ? $name : ('UPID #' . $id);
    }
    return $map;
}

/**
 * Alle Fahrzeuge inkl. Owner (wenn im XML vorhanden).
 */
function scum_admin_list_all_vehicles(bool $onlyEmpty = false, string $q = ''): array
{
    $db = getScumDbOrNull();
    if (!$db) return [];

    try {
        $stmt = $db->prepare("
            SELECT
                vs.vehicle_asset_id,
                vs.vehicle_entity_id,
                vs.vehicle_last_access_time AS vehicle_last_access,
                ie.xml AS xml_blob
            FROM vehicle_spawner vs
            JOIN vehicle_entity ve ON vs.vehicle_entity_id = ve.entity_id
            JOIN item_entity   ie ON ve.item_container_entity_id = ie.entity_id
            ORDER BY vs.vehicle_last_access_time DESC
        ");

        $res = $stmt->execute();
        if (!$res) return [];

        $rows = [];
        $ownerIds = [];

        while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
            $asset = (string)($row['vehicle_asset_id'] ?? '');
            $entityId = (string)($row['vehicle_entity_id'] ?? '');
            $xml = (string)($row['xml_blob'] ?? '');
            $lastAccessRaw = $row['vehicle_last_access'] ?? null;

            $ownerUpid = scum_vehicle_extract_owner_upid($xml);
            if ($ownerUpid) $ownerIds[] = $ownerUpid;

            $name = str_replace("Vehicle:BPC_", "", $asset);
            if ($name === '' && $asset !== '') $name = $asset;
            if ($name === '') $name = 'Unknown';

            $lastAccess = 'Unbekannt';
            if (is_numeric($lastAccessRaw) && (int)$lastAccessRaw > 0) {
                $lastAccess = date('d.m.Y H:i', (int)$lastAccessRaw);
            }

            $rows[] = [
                'vehicle_name' => $name,
                'vehicle_asset_id' => $asset,
                'vehicle_entity_id' => $entityId,
                'owner_user_profile_id' => $ownerUpid,
                'owner_name' => null,
                'is_empty' => $ownerUpid ? 0 : 1,
                'last_access' => $lastAccess,
            ];
        }

        $nameMap = scum_get_user_profile_names_by_ids($ownerIds);
        foreach ($rows as &$r) {
            $upid = (int)($r['owner_user_profile_id'] ?? 0);
            if ($upid > 0) $r['owner_name'] = $nameMap[$upid] ?? ('UPID #' . $upid);
        }
        unset($r);

        if ($onlyEmpty) {
            $rows = array_values(array_filter($rows, fn($r) => !empty($r['is_empty'])));
        }

        $q = trim($q);
        if ($q !== '') {
            $qq = mb_strtolower($q);
            $rows = array_values(array_filter($rows, function ($r) use ($qq) {
                $hay = mb_strtolower(
                    (string)$r['vehicle_name'] . ' ' .
                    (string)$r['vehicle_asset_id'] . ' ' .
                    (string)$r['vehicle_entity_id'] . ' ' .
                    (string)($r['owner_name'] ?? '') . ' ' .
                    (string)($r['owner_user_profile_id'] ?? '')
                );
                return mb_strpos($hay, $qq) !== false;
            }));
        }

        return $rows;

    } catch (Throwable $e) {
        scum_db_mark_syncing('copy_in_progress_or_malformed');
        return [];
    }
}
