<?php
//scum_user_function.php
declare(strict_types=1);

require_once __DIR__ . '/scum_db.php';

function scum_get_user_profile_by_steamid(string $steamId): ?array
{
    $db = getScumDbOrNull();
    if (!$db) return null;

    try {
        $stmt = $db->prepare("
            SELECT
                id,
                user_id,
                name,
                template_xml,
                money_balance,
                play_time,
                last_login_time,
                last_logout_time,
                prisoner_id,
                fame_points,
                fake_name,
                creation_time
            FROM user_profile
            WHERE user_id = :steamid
            LIMIT 1
        ");

        $stmt->bindValue(':steamid', $steamId, SQLITE3_TEXT);
        $res = $stmt->execute();
        if (!$res) return null;

        $row = $res->fetchArray(SQLITE3_ASSOC);
        return $row ?: null;

    } catch (Throwable $e) {
        // DB ist vermutlich gerade im Transfer -> Flag setzen, damit Header-Modal erscheint
        scum_db_mark_syncing('copy_in_progress_or_malformed');
        return null;
    }
}


function scum_parse_character_template(?string $xml): array
{
    $out = [
        'name' => null,
        'age'  => null,
        'str'  => null,
        'con'  => null,
        'dex'  => null,
        'int'  => null,
        'skills' => [
            'STR' => [],
            'CON' => [],
            'DEX' => [],
            'INT' => [],
        ],
    ];

    if ($xml === null) return $out;

    // 1) NULL-Bytes entfernen (typisch bei UTF-16/SQLite)
    $xml = str_replace("\x00", '', $xml);
    $xml = trim($xml);

    if ($xml === '') return $out;

    // 2) Falls UTF-16 BOM drin ist -> nach UTF-8 konvertieren
    // (nach dem \x00 strip kann BOM manchmal noch sichtbar bleiben)
    $bom2 = substr($xml, 0, 2);
    if ($bom2 === "\xFF\xFE" || $bom2 === "\xFE\xFF") {
        if (function_exists('mb_convert_encoding')) {
            $xml = mb_convert_encoding($xml, 'UTF-8', 'UTF-16');
        }
    }

    // 3) Falls kein <CharacterTemplate> am Anfang steht, trotzdem versuchen den Tag zu finden
    $pos = strpos($xml, '<CharacterTemplate');
    if ($pos !== false && $pos > 0) {
        $xml = substr($xml, $pos);
    }

    // Sicher parsen (kein Netzwerk, keine Warnungen)
    $prev = libxml_use_internal_errors(true);
    $sxe  = simplexml_load_string($xml, 'SimpleXMLElement', LIBXML_NONET | LIBXML_NOERROR | LIBXML_NOWARNING);
    libxml_clear_errors();
    libxml_use_internal_errors($prev);

    if (!$sxe) return $out;

    $attrs = $sxe->attributes();

    $out['name'] = (string)($attrs['Name'] ?? '') ?: null;
    $out['age']  = isset($attrs['Age']) ? (int)$attrs['Age'] : null;

    $out['str'] = isset($attrs['Strength'])     ? (float)$attrs['Strength']     : null;
    $out['con'] = isset($attrs['Constitution']) ? (float)$attrs['Constitution'] : null;
    $out['dex'] = isset($attrs['Dexterity'])    ? (float)$attrs['Dexterity']    : null;
    $out['int'] = isset($attrs['Intelligence']) ? (float)$attrs['Intelligence'] : null;

    // Skill-ClassName -> Kategorie + Label
    $skillMap = [
        'BoxingSkill'       => ['STR', 'Brawling'],
        'MeleeWeaponsSkill' => ['STR', 'Melee Weapons'],
        'ArcherySkill'      => ['STR', 'Archery'],

        'RunningSkill'      => ['CON', 'Running'],
        'EnduranceSkill'    => ['CON', 'Endurance'],
        'ResistanceSkill'   => ['CON', 'Resistance'],

        'StealthSkill'      => ['DEX', 'Stealth'],
        'ThieverySkill'     => ['DEX', 'Thievery'],
        'DrivingSkill'      => ['DEX', 'Driving'],

        'RiflesSkill'       => ['INT', 'Rifles'],
        'HandgunSkill'      => ['INT', 'Handgun'],
        'AwarenessSkill'    => ['INT', 'Awareness'],
    ];

    foreach ($sxe->Skill as $skill) {
        $sa = $skill->attributes();
        $class = (string)($sa['ClassName'] ?? '');
        if ($class === '' || !isset($skillMap[$class])) continue;

        [$cat, $label] = $skillMap[$class];

        $out['skills'][$cat][] = [
            'label' => $label,
            'level' => isset($sa['Level']) ? (int)$sa['Level'] : 0,
            'xp'    => isset($sa['Experience']) ? (float)$sa['Experience'] : 0.0,
        ];
    }

    return $out;
}
function scum_get_balances_by_user_profile_id(int $userProfileId): array
{
    $out = [
        'kuna' => 0,
        'gold' => 0,
    ];

    if ($userProfileId <= 0) return $out;

    $db = getScumDb();

    $stmt = $db->prepare("
        SELECT currency_type, account_balance
        FROM bank_account_registry_currencies
        WHERE bank_account_id = :upid
    ");
    $stmt->bindValue(':upid', $userProfileId, SQLITE3_INTEGER);

    $res = $stmt->execute();
    if (!$res) return $out;

    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $type = (int)($row['currency_type'] ?? 0);
        $bal  = (int)($row['account_balance'] ?? 0);

        if ($type === 1) $out['kuna'] = $bal;
        if ($type === 2) $out['gold'] = $bal;
    }

    return $out;
}
function scum_get_events_stats_by_user_profile_id(int $userProfileId): array
{
    $out = [
        'events_won'   => 0,
        'events_lost'  => 0,
        'enemy_kills'  => 0,
        'deaths'       => 0,
    ];

    if ($userProfileId <= 0) return $out;

    $db = getScumDb();

    $stmt = $db->prepare("
        SELECT events_won, events_lost, enemy_kills, deaths
        FROM events_stats
        WHERE user_profile_id = :upid
        LIMIT 1
    ");
    $stmt->bindValue(':upid', $userProfileId, SQLITE3_INTEGER);

    $res = $stmt->execute();
    if (!$res) return $out;

    $row = $res->fetchArray(SQLITE3_ASSOC);
    if (!$row) return $out;

    return [
        'events_won'  => (int)($row['events_won'] ?? 0),
        'events_lost' => (int)($row['events_lost'] ?? 0),
        'enemy_kills' => (int)($row['enemy_kills'] ?? 0),
        'deaths'      => (int)($row['deaths'] ?? 0),
    ];
}

function scum_get_survival_stats_by_user_profile_id(int $userProfileId): array
{
    if ($userProfileId <= 0) return [];

    $db = getScumDb();

    $stmt = $db->prepare("
        SELECT *
        FROM survival_stats
        WHERE user_profile_id = :upid
        LIMIT 1
    ");
    $stmt->bindValue(':upid', $userProfileId, SQLITE3_INTEGER);

    $res = $stmt->execute();
    if (!$res) return [];

    $row = $res->fetchArray(SQLITE3_ASSOC);
    return is_array($row) ? $row : [];
}


function scum_format_seconds_hms(int $seconds): string
{
    if ($seconds < 0) $seconds = 0;
    $h = intdiv($seconds, 3600);
    $m = intdiv($seconds % 3600, 60);
    $s = $seconds % 60;
    return sprintf('%02d:%02d:%02d', $h, $m, $s);
}
function scum_get_locked_vehicles_by_user_profile_id(int $userProfileId): array
{
    if ($userProfileId <= 0) return [];

    $db = getScumDb();

    // quick locked-check (wie dein alter Code)
    $isVehicleLocked = function (string $xml): bool {
        if ($xml === '') return false;

        $prev = libxml_use_internal_errors(true);
        $x = simplexml_load_string($xml);
        libxml_clear_errors();
        libxml_use_internal_errors($prev);

        if ($x !== false && isset($x->Locks)) {
            $slots = $x->Locks->xpath('LockSlot');
            if (is_array($slots) && count($slots) > 0) return true;
        }

        $t = strtolower($xml);
        if (strpos($t, '<lockslot') !== false) return true;
        if (preg_match('/\b(islocked|locked)\s*=\s*"(true|1)"/i', $xml)) return true;
        if (preg_match('/\blockstate\s*=\s*"locked"/i', $xml)) return true;
        if (preg_match('/\b(hascodelock|hascodelock)\s*=\s*"true"/i', $xml)) return true;
        if (preg_match('/codelock/i', $xml)) return true;
        if (preg_match('/doorlock|lockcomponent/i', $xml)) return true;

        return false;
    };

    // Query wie in fetch_playerinfo()
    $stmt = $db->prepare("
        SELECT DISTINCT
            vs.vehicle_asset_id,
            vs.vehicle_entity_id,
            vs.vehicle_last_access_time AS vehicle_last_access,
            ie.xml AS xml_blob
        FROM vehicle_spawner vs
        JOIN vehicle_entity ve ON vs.vehicle_entity_id = ve.entity_id
        JOIN item_entity   ie ON ve.item_container_entity_id = ie.entity_id
        WHERE (ie.xml LIKE :ownU OR ie.xml LIKE :own)
    ");

    $stmt->bindValue(':ownU', '%_owningUserProfileId="' . $userProfileId . '"%', SQLITE3_TEXT);
    $stmt->bindValue(':own',  '%owningUserProfileId="'  . $userProfileId . '"%', SQLITE3_TEXT);

    $res = $stmt->execute();
    if (!$res) return [];

    $vehicles = [];
    $seen = [];

    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $xml = (string)($row['xml_blob'] ?? '');
        if (!$isVehicleLocked($xml)) continue;

        $asset = (string)($row['vehicle_asset_id'] ?? '');
        $entityId = (string)($row['vehicle_entity_id'] ?? '');
        $lastAccessRaw = $row['vehicle_last_access'] ?? null;

        $name = str_replace("Vehicle:BPC_", "", $asset);
        if ($name === '') $name = 'Unknown';

        $key = $name . '#' . $entityId;
        if (isset($seen[$key])) continue;
        $seen[$key] = true;

        $lastAccess = 'Unbekannt';
        if (is_numeric($lastAccessRaw) && (int)$lastAccessRaw > 0) {
            $lastAccess = date('d.m.Y H:i', (int)$lastAccessRaw);
        }

        $vehicles[] = [
            'name' => $name,
            'id'   => $entityId,
            'last_access' => $lastAccess,
        ];
    }

    return $vehicles;
}
function scum_get_squad_by_user_profile_id(int $userProfileId): ?array
{
    if ($userProfileId <= 0) return null;

    $db = getScumDb();

    // Squad-Mitgliedschaft + Rang
    $stmt = $db->prepare("
        SELECT squad_id, rank
        FROM squad_member
        WHERE user_profile_id = :upid
        LIMIT 1
    ");
    $stmt->bindValue(':upid', $userProfileId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    $member = $res ? $res->fetchArray(SQLITE3_ASSOC) : null;

    if (!$member || empty($member['squad_id'])) return null;

    $squadId = (int)$member['squad_id'];
    $rank    = (string)$member['rank'];

    // Squad-Name
    $stmt = $db->prepare("SELECT name FROM squad WHERE id = :sid LIMIT 1");
    $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    $row = $res ? $res->fetchArray(SQLITE3_ASSOC) : null;

    $name = $row['name'] ?? 'Unbekannt';

    // Mitglieder zählen
    $stmt = $db->prepare("
        SELECT COUNT(*) AS cnt
        FROM squad_member
        WHERE squad_id = :sid
    ");
    $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    $countRow = $res ? $res->fetchArray(SQLITE3_ASSOC) : null;
    $memberCount = (int)($countRow['cnt'] ?? 0);

    // Squad-Famepoints (Summe)
    $stmt = $db->prepare("
        SELECT SUM(u.fame_points) AS total_fame
        FROM squad_member sm
        JOIN user_profile u ON sm.user_profile_id = u.id
        WHERE sm.squad_id = :sid
    ");
    $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    $fameRow = $res ? $res->fetchArray(SQLITE3_ASSOC) : null;
    $totalFame = (int)($fameRow['total_fame'] ?? 0);

    return [
        'id'        => $squadId,
        'name'      => $name,
        'rank'      => $rank,
        'members'   => $memberCount,
        'fame'      => $totalFame,
    ];
}
function scum_get_prisoner_body_blob_by_user_profile_id(int $userProfileId, ?int $prisonerId = null): ?string
{
    if ($userProfileId <= 0) return null;

    $db = getScumDb();

    // 1) Primär: prisoner.user_profile_id
    $stmt = $db->prepare("
        SELECT body_simulation
        FROM prisoner
        WHERE user_profile_id = :upid
        LIMIT 1
    ");
    $stmt->bindValue(':upid', $userProfileId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    $row = $res ? $res->fetchArray(SQLITE3_ASSOC) : null;

    if (!empty($row['body_simulation'])) {
        return $row['body_simulation'];
    }

    // 2) Fallback: prisoner.id = user_profile.prisoner_id
    if ($prisonerId !== null && $prisonerId > 0) {
        $stmt = $db->prepare("
            SELECT body_simulation
            FROM prisoner
            WHERE id = :pid
            LIMIT 1
        ");
        $stmt->bindValue(':pid', $prisonerId, SQLITE3_INTEGER);
        $res = $stmt->execute();
        $row = $res ? $res->fetchArray(SQLITE3_ASSOC) : null;

        if (!empty($row['body_simulation'])) {
            return $row['body_simulation'];
        }
    }

    return null;
}


function scum_extract_double_after_key(string $blob, string $key): ?float
{
    $keyPos = strpos($blob, $key);
    if ($keyPos === false) return null;

    // Gist / SCUM BodySim Layout
    $KEY_PADDING   = 5;
    $VALUE_PADDING = 10;
    $typeName      = 'DoubleProperty';

    $typePos = $keyPos + strlen($key) + $KEY_PADDING;

    // Type check (muss exakt passen)
    if (substr($blob, $typePos, strlen($typeName)) !== $typeName) {
        return null;
    }

    $valuePos = $typePos + strlen($typeName) + $VALUE_PADDING;

    if (strlen($blob) < $valuePos + 8) return null;

    $valBytes = substr($blob, $valuePos, 8);

    // little-endian double
    $arr = unpack('e', $valBytes);
    return $arr[1] ?? null;
}


function scum_debug_double_gist(string $blob, string $key): array
{
    $KEY_PADDING   = 5;
    $VALUE_PADDING = 10;
    $type = "DoubleProperty";

    $out = [
        'key' => $key,
        'keyOffset' => null,
        'typeOffset' => null,
        'valueOffset' => null,
        'typeOk' => false,
        'valueHex' => null,
        'value' => null,
    ];

    $pos = strpos($blob, $key);
    if ($pos === false) return $out;

    $out['keyOffset'] = $pos;

    $typePos = $pos + strlen($key) + $KEY_PADDING;
    $out['typeOffset'] = $typePos;

    $out['typeOk'] = (substr($blob, $typePos, strlen($type)) === $type);

    $valuePos = $typePos + strlen($type) + $VALUE_PADDING;
    $out['valueOffset'] = $valuePos;

    if (strlen($blob) >= $valuePos + 8) {
        $valBytes = substr($blob, $valuePos, 8);
        $out['valueHex'] = bin2hex($valBytes);
        $out['value'] = unpack('e', $valBytes)[1] ?? null;
    }

    return $out;
}


function scum_debug_parse_double_property(string $blob, string $key): array
{
    $out = [
        'key' => $key,
        'key_offset' => null,
        'after_key_byte' => null,
        'lenType' => null,
        'typeName' => null,
        'propSize' => null,
        'arrayIndex' => null,
        'valueHex' => null,
        'value' => null,
        'ok' => false,
        'error' => null,
    ];

    $pos = strpos($blob, $key);
    if ($pos === false) {
        $out['error'] = 'key not found';
        return $out;
    }
    $out['key_offset'] = $pos;

    $i = $pos + strlen($key);

    // expect null terminator after key
    $out['after_key_byte'] = isset($blob[$i]) ? bin2hex($blob[$i]) : null;
    if (!isset($blob[$i]) || $blob[$i] !== "\x00") {
        $out['error'] = 'missing null terminator after key';
        return $out;
    }
    $i += 1;

    if (strlen($blob) < $i + 4) {
        $out['error'] = 'blob too short for lenType';
        return $out;
    }

    $lenType = unpack('V', substr($blob, $i, 4))[1]; // uint32 little
    $out['lenType'] = $lenType;
    $i += 4;

    if ($lenType <= 0 || strlen($blob) < $i + $lenType) {
        $out['error'] = 'invalid lenType or blob too short for typeName';
        return $out;
    }

    $typeRaw = substr($blob, $i, $lenType); // includes \0 usually
    $i += $lenType;

    $typeName = rtrim($typeRaw, "\x00");
    $out['typeName'] = $typeName;

    if ($typeName !== 'DoubleProperty') {
        $out['error'] = 'typeName is not DoubleProperty';
        return $out;
    }

    // Next: propSize (int32) + arrayIndex (int32)
    if (strlen($blob) < $i + 8) {
        $out['error'] = 'blob too short for propSize/arrayIndex';
        return $out;
    }

    $out['propSize'] = unpack('V', substr($blob, $i, 4))[1];
    $out['arrayIndex'] = unpack('V', substr($blob, $i + 4, 4))[1];
    $i += 8;

    // Next: double (8 bytes)
    if (strlen($blob) < $i + 8) {
        $out['error'] = 'blob too short for double value';
        return $out;
    }

    $valBytes = substr($blob, $i, 8);
    $out['valueHex'] = bin2hex($valBytes);
    $out['value'] = unpack('g', $valBytes)[1]; // little-endian double
    $out['ok'] = true;

    return $out;
}


function scum_get_live_attributes_from_body_blob(?string $blob): array
{
    return [
        'str' => $blob ? scum_extract_double_after_key($blob, 'BaseStrength') : null,
        'con' => $blob ? scum_extract_double_after_key($blob, 'BaseConstitution') : null,
        'dex' => $blob ? scum_extract_double_after_key($blob, 'BaseDexterity') : null,
        'int' => $blob ? scum_extract_double_after_key($blob, 'BaseIntelligence') : null,
    ];
}

function scum_get_squad_members_by_squad_id(int $squadId): array
{
    if ($squadId <= 0) return [];

    $db = getScumDb();

    $stmt = $db->prepare("
        SELECT
            u.name AS name,
            sm.rank AS rank,
            u.fame_points AS fame,
            u.play_time AS playtime
        FROM squad_member sm
        JOIN user_profile u ON sm.user_profile_id = u.id
        WHERE sm.squad_id = :sid
        ORDER BY sm.rank DESC, u.fame_points DESC, u.name COLLATE NOCASE ASC
    ");
    $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);

    $res = $stmt->execute();
    if (!$res) return [];

    $out = [];
    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $out[] = [
            'name' => (string)($row['name'] ?? ''),
            'rank' => (int)($row['rank'] ?? 0),
            'fame' => (int)($row['fame'] ?? 0),
            'playtime' => (int)($row['playtime'] ?? 0),
        ];
    }

    return $out;
}
function scum_get_locked_vehicles_by_squad_id(int $squadId): array
{
    if ($squadId <= 0) return [];

    $db = getScumDb();

    // Squad-Mitglieder (user_profile_id + Name)
    $stmt = $db->prepare("
        SELECT u.id AS user_profile_id, u.name AS name
        FROM squad_member sm
        JOIN user_profile u ON sm.user_profile_id = u.id
        WHERE sm.squad_id = :sid
    ");
    $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    if (!$res) return [];

    $members = [];
    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $upid = (int)($row['user_profile_id'] ?? 0);
        if ($upid <= 0) continue;
        $members[] = [
            'id' => $upid,
            'name' => (string)($row['name'] ?? 'Unbekannt'),
        ];
    }

    if (!$members) return [];

    // Für jedes Member die LOCKED Vehicles holen, dann zusammenführen
    $out = [];
    $seen = []; // dedupe global (vehicleEntityId reicht i.d.R.)
    foreach ($members as $m) {
        $list = scum_get_locked_vehicles_by_user_profile_id((int)$m['id']);
        foreach ($list as $v) {
            $entityId = (string)($v['id'] ?? '');
            if ($entityId === '') continue;

            // dedupe über Entity-ID
            if (isset($seen[$entityId])) continue;
            $seen[$entityId] = true;

            $out[] = [
                'owner' => $m['name'],
                'name' => (string)($v['name'] ?? 'Unknown'),
                'id' => $entityId,
                'last_access' => (string)($v['last_access'] ?? 'Unbekannt'),
            ];
        }
    }

    // optional sort: Owner -> Name
    usort($out, function ($a, $b) {
        $c = strcasecmp((string)$a['owner'], (string)$b['owner']);
        if ($c !== 0) return $c;
        return strcasecmp((string)$a['name'], (string)$b['name']);
    });

    return $out;
}
function scum_get_names_by_steamids(array $steamIds): array
{
    $steamIds = array_values(array_unique(array_filter(array_map('strval', $steamIds))));
    if (!$steamIds) return [];

    $db = getScumDb(); // SQLite3

    // IN (?, ?, ?)
    $placeholders = implode(',', array_fill(0, count($steamIds), '?'));

    // Wenn der Name NICHT in user_profile liegt, sondern in prisoner, sag kurz Bescheid.
    // Für deinen aktuellen Stand: user_profile.user_id = SteamID, user_profile.name = Name
    $stmt = $db->prepare("
        SELECT user_id, name
        FROM user_profile
        WHERE user_id IN ($placeholders)
    ");

    foreach ($steamIds as $i => $sid) {
        // SQLite3 bind index startet bei 1
        $stmt->bindValue($i + 1, $sid, SQLITE3_TEXT);
    }

    $res = $stmt->execute();
    if (!$res) return [];

    $map = [];
    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $sid = (string)($row['user_id'] ?? '');
        $name = (string)($row['name'] ?? '');
        if ($sid !== '') $map[$sid] = $name;
    }

    return $map;
}
