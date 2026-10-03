<?php
// api/gym_push.php
declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');

function gp_json(array $data, int $status = 200): void
{
    http_response_code($status);
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function gp_load_env_file(string $file): void
{
    if (!is_file($file)) return;

    $lines = file($file, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES);
    if (!$lines) return;

    foreach ($lines as $line) {
        $line = trim($line);
        if ($line === '' || str_starts_with($line, '#')) continue;
        if (!str_contains($line, '=')) continue;

        [$key, $value] = explode('=', $line, 2);
        $key = trim($key);
        $value = trim($value);

        if (
            (str_starts_with($value, '"') && str_ends_with($value, '"')) ||
            (str_starts_with($value, "'") && str_ends_with($value, "'"))
        ) {
            $value = substr($value, 1, -1);
        }

        putenv($key . '=' . $value);
        $_ENV[$key] = $value;
    }
}

function gp_env(string $key, string $default = ''): string
{
    $v = getenv($key);
    if ($v !== false && $v !== '') return (string)$v;
    if (isset($_ENV[$key]) && $_ENV[$key] !== '') return (string)$_ENV[$key];
    return $default;
}

function gp_auth_header(): string
{
    $headers = function_exists('getallheaders') ? getallheaders() : [];

    foreach ($headers as $k => $v) {
        if (strtolower((string)$k) === 'authorization') {
            return trim((string)$v);
        }
    }

    return trim((string)($_SERVER['HTTP_AUTHORIZATION'] ?? ''));
}

function gp_safe_steamid(string $value): string
{
    $value = trim($value);
    $value = preg_replace('/[^0-9]/', '', $value) ?? '';

    if ($value === '') return 'unknown';

    return $value;
}

function gp_data_dir(): string
{
    $envDir = gp_env('GYM_DATA_DIR');
    if ($envDir !== '') {
        return rtrim($envDir, '/\\');
    }

    $privateDir = realpath(__DIR__ . '/../private');
    if ($privateDir !== false) {
        return $privateDir . '/gym_data';
    }

    return __DIR__ . '/../private/gym_data';
}

function gp_players_dir(): string
{
    return gp_data_dir() . '/players';
}

function gp_meta_file(): string
{
    return gp_data_dir() . '/meta.json';
}

function gp_player_file(string $steamId): string
{
    return gp_players_dir() . '/' . gp_safe_steamid($steamId) . '.json';
}

function gp_read_json(string $file, array $default = []): array
{
    if (!is_file($file)) return $default;

    $raw = file_get_contents($file);
    if ($raw === false || trim($raw) === '') return $default;

    $data = json_decode($raw, true);
    return is_array($data) ? $data : $default;
}

function gp_write_json(string $file, array $data): bool
{
    $dir = dirname($file);

    if (!is_dir($dir)) {
        mkdir($dir, 0755, true);
    }

    return file_put_contents(
        $file,
        json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES),
        LOCK_EX
    ) !== false;
}

function gp_snapshot_signature(array $snap): string
{
    $copy = $snap;
    unset($copy['capturedAt']);
    return md5(json_encode($copy, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
}

gp_load_env_file(__DIR__ . '/../private/.env');

$expectedToken = gp_env('GYM_PUSH_TOKEN');
if ($expectedToken === '') {
    gp_json(['ok' => false, 'error' => 'missing_server_push_token'], 500);
}

$givenToken = trim((string)($_SERVER['HTTP_X_GYM_TOKEN'] ?? ''));
$auth = gp_auth_header();

if ($givenToken === '' && stripos($auth, 'Bearer ') === 0) {
    $givenToken = trim(substr($auth, 7));
}

if ($givenToken === '' && isset($_GET['token'])) {
    $givenToken = trim((string)$_GET['token']);
}

if ($givenToken === '' || !hash_equals($expectedToken, $givenToken)) {
    gp_json(['ok' => false, 'error' => 'forbidden'], 403);
}

$raw = file_get_contents('php://input');
$payload = json_decode($raw ?: '', true);

if (!is_array($payload)) {
    gp_json(['ok' => false, 'error' => 'invalid_json'], 400);
}

$players = $payload['players'] ?? [];

if (!is_array($players)) {
    gp_json(['ok' => false, 'error' => 'players_missing'], 400);
}

$capturedAt = (string)($payload['capturedAt'] ?? gmdate('c'));
$onlinePlayers = (int)($payload['onlinePlayers'] ?? count($players));

$maxSnapshots = (int)gp_env('GYM_MAX_SNAPSHOTS', '1000');
if ($maxSnapshots < 10) $maxSnapshots = 1000;

$saveUnchanged = gp_env('GYM_SAVE_UNCHANGED', '0') === '1';

$saved = 0;
$skipped = 0;
$writtenFiles = [];

foreach ($players as $player) {
    if (!is_array($player)) {
        $skipped++;
        continue;
    }

    $steamId = gp_safe_steamid((string)($player['steamId'] ?? $player['userId'] ?? ''));
    if ($steamId === 'unknown') {
        $skipped++;
        continue;
    }

    $snap = $player['snapshot'] ?? null;

    if (!is_array($snap)) {
        $skills = $player['skills'] ?? null;
        $attributes = $player['attributes'] ?? null;

        if ($skills === null && $attributes === null) {
            $skipped++;
            continue;
        }

        $snap = [
            'capturedAt' => $capturedAt,
            'attributes' => $attributes ?? [],
            'skills' => $skills ?? [],
        ];
    }

    $snap['capturedAt'] = (string)($snap['capturedAt'] ?? $capturedAt);

    $file = gp_player_file($steamId);

    $existing = gp_read_json($file, [
        'steamId' => $steamId,
        'characterName' => null,
        'steamName' => null,
        'updatedAt' => null,
        'lastSeenOnline' => null,
        'snapshots' => [],
    ]);

    $snapshots = $existing['snapshots'] ?? [];
    if (!is_array($snapshots)) $snapshots = [];

    $prev = $snapshots ? $snapshots[count($snapshots) - 1] : null;
    $changed = !$prev || gp_snapshot_signature($prev) !== gp_snapshot_signature($snap);

    if ($changed || $saveUnchanged) {
        $snapshots[] = $snap;

        while (count($snapshots) > $maxSnapshots) {
            array_shift($snapshots);
        }

        $existing['snapshots'] = $snapshots;
        $existing['updatedAt'] = $capturedAt;
        $saved++;
    }

    $existing['steamId'] = $steamId;
    $existing['characterName'] = $player['characterName'] ?? $player['name'] ?? $existing['characterName'] ?? null;
    $existing['steamName'] = $player['steamName'] ?? $existing['steamName'] ?? null;
    $existing['lastSeenOnline'] = $capturedAt;

    gp_write_json($file, $existing);
    $writtenFiles[] = $file;
}

gp_write_json(gp_meta_file(), [
    'ok' => true,
    'updatedAt' => $capturedAt,
    'lastRunAt' => $capturedAt,
    'onlinePlayers' => $onlinePlayers,
    'savedSnapshots' => $saved,
    'skippedPlayers' => $skipped,
    'lastError' => null,
    'source' => 'windows-push',
]);

gp_json([
    'ok' => true,
    'updatedAt' => $capturedAt,
    'receivedPlayers' => count($players),
    'savedSnapshots' => $saved,
    'skippedPlayers' => $skipped,
    'debug' => [
        'dataDir' => gp_data_dir(),
        'playersDir' => gp_players_dir(),
        'metaFile' => gp_meta_file(),
        'dataDirExists' => is_dir(gp_data_dir()),
        'playersDirExists' => is_dir(gp_players_dir()),
        'dataDirWritable' => is_dir(gp_data_dir()) ? is_writable(gp_data_dir()) : false,
        'playersDirWritable' => is_dir(gp_players_dir()) ? is_writable(gp_players_dir()) : false,
        'metaFileExists' => is_file(gp_meta_file()),
        'writtenFiles' => $writtenFiles,
        'scriptFile' => __FILE__,
        'documentRoot' => $_SERVER['DOCUMENT_ROOT'] ?? null,
    ],
]);