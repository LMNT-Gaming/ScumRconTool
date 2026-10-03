<?php

declare(strict_types=1);

// === Quest-Config ===
const QUEST_DIR = __DIR__ . '/../Quests'; // ggf. anpassen (dein aktueller map.php liegt unter /pages)

// --- kleine Helfer ---
function q_pick(array $a, array $keys)
{
    foreach ($keys as $k) {
        if (array_key_exists($k, $a) && $a[$k] !== null && $a[$k] !== '') return $a[$k];
    }
    return null;
}
function q_normalize_npc(?string $s): string
{
    $v = trim((string)$s);
    if ($v === '') return 'Unknown';
    $v = preg_replace('/\s+/', ' ', $v);
    $map = [
        'armorer' => 'Armorer',
        'banker' => 'Banker',
        'bartender' => 'Bartender',
        'doctor' => 'Doctor',
        'mechanic' => 'Mechanic',
        'merchant' => 'Merchant',
    ];
    $low = strtolower($v);
    return $map[$low] ?? mb_convert_case($v, MB_CASE_TITLE, 'UTF-8');
}
function q_extract_npc(array $j): string
{
    $candidate = q_pick($j, ['AssociatedNPC', 'AssociatedNpc', 'NPC', 'Trader', 'Vendor', 'Associated_NPC']);
    return q_normalize_npc(is_string($candidate) ? $candidate : null);
}
function q_parse_fallback_transform(?string $ft): array
{
    // "x,y,z|pitch,yaw,roll|scale" (bei dir in den Questfiles)
    $ft = (string)$ft;
    $parts = explode('|', $ft);
    $pos = explode(',', $parts[0] ?? '');
    return [
        'x' => isset($pos[0]) ? (float)$pos[0] : 0.0,
        'y' => isset($pos[1]) ? (float)$pos[1] : 0.0,
        'z' => isset($pos[2]) ? (float)$pos[2] : 0.0,
    ];
}

function q_read_quests_with_points(string $dir): array
{
    if (!is_dir($dir)) return [];
    $files = glob($dir . '/*.json') ?: [];
    $out = [];

    foreach ($files as $f) {
        $raw = @file_get_contents($f);
        if ($raw === false) continue;

        $j = json_decode($raw, true);
        if (!is_array($j)) continue;

        $npc   = q_extract_npc($j);
        $tier  = (int)($j['Tier'] ?? 1);
        $title = (string)($j['Title'] ?? basename($f));
        $desc  = (string)($j['Description'] ?? '');
        $mtime = (int)(@filemtime($f) ?: 0);

        $points = [];
        $conds = $j['Conditions'] ?? [];
        if (is_array($conds)) {
            foreach ($conds as $c) {
                if (!is_array($c)) continue;
                $type = (string)($c['Type'] ?? '');
                if ($type !== 'Interaction') continue;

                $seq = isset($c['SequenceIndex']) ? (int)$c['SequenceIndex'] : null;
                $cap = trim((string)($c['TrackingCaption'] ?? ''));

                $locs = $c['Locations'] ?? [];
                if (!is_array($locs)) continue;

                foreach ($locs as $L) {
                    if (!is_array($L)) continue;
                    $ft = (string)($L['FallbackTransform'] ?? '');
                    $p = q_parse_fallback_transform($ft);

                    // wir nehmen X/Y aus Transform (SCUM world coords)
                    $points[] = [
                        'x' => $p['x'],
                        'y' => $p['y'],
                        'seq' => $seq,
                        'cap' => $cap,
                    ];
                }
            }
        }

        $rewards = $j['RewardPool'] ?? [];
        if (!is_array($rewards)) $rewards = [];

        $conds = $j['Conditions'] ?? [];
        if (!is_array($conds)) $conds = [];

        $out[] = [
            'id'     => sha1(basename($f)),
            'file'   => basename($f),
            'mtime'  => $mtime,
            'npc'    => $npc,
            'tier'   => $tier,
            'title'  => $title,
            'desc'   => $desc,

            'points' => $points,

            // ✅ neu:
            'rewards' => $rewards,     // Frontend: q.rewards
            'conds'   => $conds,       // Frontend: q.conds (optional, aber praktisch)

            // optional doppelt für Kompatibilität:
            'RewardPool'  => $rewards,
            'Conditions'  => $conds,
        ];
    }

    return $out;
}

// === Daten laden ===
$quests = q_read_quests_with_points(QUEST_DIR);
