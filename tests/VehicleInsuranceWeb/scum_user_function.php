<?php
// Offline fixture: tests ownership routing without touching a real SCUM database.
function scum_db_status(): array { return ['ok'=>!is_file(__DIR__.'/../db-unavailable')]; }
function scum_get_user_profile_by_steamid(string $steam): ?array {
    return match ($steam) {
        '76561198000000001'=>['id'=>1],
        '76561198000000002'=>['id'=>2],
        default=>null
    };
}
function scum_get_locked_vehicles_by_user_profile_id(int $id): array {
    return $id===1
        ? [['id'=>'6382416','name'=>'Barba','last_access'=>'01.09.2026 12:00'],['id'=>'7777777','name'=>'Laika','last_access'=>'Unbekannt']]
        : [['id'=>'9999999','name'=>'Other owner vehicle','last_access'=>'Unbekannt']];
}
