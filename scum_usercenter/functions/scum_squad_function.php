<?php
declare(strict_types=1);

require_once __DIR__ . '/scum_db.php';

function scum_get_squad_with_members(int $squadId): ?array
{
    if ($squadId <= 0) return null;

    $db = getScumDb();

    // Squad Basisdaten
    $stmt = $db->prepare("
        SELECT id, name
        FROM squad
        WHERE id = :sid
        LIMIT 1
    ");
    $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $res = $stmt->execute();
    $squad = $res ? $res->fetchArray(SQLITE3_ASSOC) : null;

    if (!$squad) return null;

    // Mitglieder + Rang + Fame + Playtime
    $stmt = $db->prepare("
        SELECT
            u.id AS user_profile_id,
            u.name,
            u.fame_points,
            u.play_time,
            sm.rank
        FROM squad_member sm
        JOIN user_profile u ON sm.user_profile_id = u.id
        WHERE sm.squad_id = :sid
        ORDER BY sm.rank DESC, u.name ASC
    ");
    $stmt->bindValue(':sid', $squadId, SQLITE3_INTEGER);
    $res = $stmt->execute();

    $members = [];
    $totalFame = 0;

    while ($row = $res->fetchArray(SQLITE3_ASSOC)) {
        $totalFame += (int)$row['fame_points'];

        $members[] = [
            'name'       => $row['name'],
            'rank'       => $row['rank'],
            'fame'       => (int)$row['fame_points'],
            'playtime'   => (int)$row['play_time'],
        ];
    }

    return [
        'id'        => $squad['id'],
        'name'      => $squad['name'],
        'members'   => $members,
        'count'     => count($members),
        'fame'      => $totalFame,
    ];
}
