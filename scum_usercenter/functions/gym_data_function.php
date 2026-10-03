<?php
// functions/gym_data_function.php
declare(strict_types=1);

function gym_data_base_dir(): string
{
    $dir = getenv('GYM_DATA_DIR') ?: ($_ENV['GYM_DATA_DIR'] ?? '');
    if ($dir !== '') return rtrim((string)$dir, '/');
    return dirname(__DIR__) . '/private/gym_data';
}

function gym_safe_steamid(string $steamId): string
{
    return preg_replace('/[^0-9]/', '', $steamId) ?: 'unknown';
}

function gym_player_file(string $steamId): string
{
    return gym_data_base_dir() . '/players/' . gym_safe_steamid($steamId) . '.json';
}

function gym_meta_file(): string
{
    return gym_data_base_dir() . '/meta.json';
}

function gym_read_json_file(string $file, array $fallback = []): array
{
    if (!is_file($file)) return $fallback;
    $raw = @file_get_contents($file);
    if ($raw === false || trim($raw) === '') return $fallback;
    $data = json_decode($raw, true);
    return is_array($data) ? $data : $fallback;
}

function gym_public_payload_for_steamid(string $steamId): array
{
    $player = gym_read_json_file(gym_player_file($steamId), [
        'steamId' => $steamId,
        'characterName' => null,
        'steamName' => null,
        'updatedAt' => null,
        'lastSeenOnline' => null,
        'snapshots' => [],
    ]);

    $meta = gym_read_json_file(gym_meta_file(), [
        'updatedAt' => null,
        'lastRunAt' => null,
        'lastError' => null,
        'onlinePlayers' => 0,
    ]);

    $snapshots = $player['snapshots'] ?? [];
    if (!is_array($snapshots)) $snapshots = [];

    // Begrenzen, damit die Seite leicht bleibt. Der Node-Sync behaelt die Rohdaten laenger.
    $snapshots = array_slice($snapshots, -250);

    return [
        'ok' => true,
        'meta' => [
            'updatedAt' => $meta['updatedAt'] ?? null,
            'lastRunAt' => $meta['lastRunAt'] ?? null,
            'lastError' => $meta['lastError'] ?? null,
            'onlinePlayers' => (int)($meta['onlinePlayers'] ?? 0),
        ],
        'player' => [
            'steamId' => $steamId,
            'characterName' => $player['characterName'] ?? null,
            'steamName' => $player['steamName'] ?? null,
            'updatedAt' => $player['updatedAt'] ?? null,
            'lastSeenOnline' => $player['lastSeenOnline'] ?? null,
            'snapshotCount' => count($snapshots),
            'latest' => $snapshots ? end($snapshots) : null,
            'snapshots' => $snapshots,
        ],
    ];
}
