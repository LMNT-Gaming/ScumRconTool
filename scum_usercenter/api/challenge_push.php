<?php
declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store, max-age=0');

function cp_reply(array $data, int $status = 200): void {
    http_response_code($status);
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}
function cp_load_env(string $file): void {
    if (!is_file($file)) return;
    foreach (file($file, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES) ?: [] as $line) {
        $line = trim($line);
        if ($line === '' || str_starts_with($line, '#') || !str_contains($line, '=')) continue;
        [$key, $value] = explode('=', $line, 2);
        $key = trim($key); $value = trim($value);
        if (($value[0] ?? '') === '"' && str_ends_with($value, '"') || ($value[0] ?? '') === "'" && str_ends_with($value, "'")) $value = substr($value, 1, -1);
        putenv($key . '=' . $value); $_ENV[$key] = $value;
    }
}
function cp_env(string $key, string $default = ''): string {
    $value = getenv($key);
    return $value !== false && $value !== '' ? (string)$value : (string)($_ENV[$key] ?? $default);
}
function cp_text(mixed $value, int $max = 300): string {
    $value = trim(preg_replace('/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/u', '', (string)$value) ?? '');
    return function_exists('mb_substr') ? mb_substr($value, 0, $max) : substr($value, 0, $max);
}
function cp_number(mixed $value, float $min = 0, float $max = 1000000000000): float {
    $number = is_numeric($value) ? (float)$value : 0;
    return max($min, min($max, $number));
}
function cp_data_dir(): string {
    $configured = trim(cp_env('CHALLENGE_DATA_DIR'));
    return $configured !== '' ? rtrim($configured, '/\\') : __DIR__ . '/../private/challenge_data';
}
function cp_token(): string {
    $token = trim((string)($_SERVER['HTTP_X_CHALLENGE_TOKEN'] ?? ''));
    $auth = trim((string)($_SERVER['HTTP_AUTHORIZATION'] ?? ''));
    if ($token === '' && stripos($auth, 'Bearer ') === 0) $token = trim(substr($auth, 7));
    return $token;
}
function cp_player(array $player, float $goalTarget): ?array {
    $steamId = preg_replace('/\D/', '', (string)($player['steamId'] ?? '')) ?? '';
    if (strlen($steamId) < 5 || strlen($steamId) > 20) return null;
    $target = cp_number($player['target'] ?? $goalTarget, 1);
    return [
        'steamId' => $steamId,
        'playerName' => cp_text($player['playerName'] ?? '', 100),
        'squadName' => cp_text($player['squadName'] ?? '', 100),
        'progress' => cp_number($player['progress'] ?? 0),
        'target' => $target,
        'percent' => cp_number($player['percent'] ?? 0, 0, 100),
        'isCompleted' => (bool)($player['isCompleted'] ?? false),
    ];
}
function cp_goal(array $goal): array {
    $target = cp_number($goal['target'] ?? 1, 1);
    $players = [];
    foreach (array_slice(is_array($goal['players'] ?? null) ? $goal['players'] : [], 0, 5000) as $player) {
        if (!is_array($player)) continue;
        $clean = cp_player($player, $target);
        if ($clean !== null) $players[] = $clean;
    }
    return [
        'key' => cp_text($goal['key'] ?? '', 150),
        'name' => cp_text($goal['name'] ?? '', 150),
        'target' => $target,
        'progress' => cp_number($goal['progress'] ?? 0),
        'percent' => cp_number($goal['percent'] ?? 0, 0, 100),
        'isCompleted' => (bool)($goal['isCompleted'] ?? false),
        'players' => $players,
    ];
}
function cp_challenge(array $challenge): array {
    $goals = [];
    foreach (array_slice(is_array($challenge['goals'] ?? null) ? $challenge['goals'] : [], 0, 20) as $goal) if (is_array($goal)) $goals[] = cp_goal($goal);
    $scope = strcasecmp((string)($challenge['goalScope'] ?? ''), 'PerPlayer') === 0 ? 'PerPlayer' : 'Community';
    $logic = strcasecmp((string)($challenge['goalLogic'] ?? ''), 'All') === 0 ? 'All' : 'Any';
    return [
        'id' => cp_text($challenge['id'] ?? '', 100),
        'type' => cp_text($challenge['type'] ?? '', 100),
        'title' => cp_text($challenge['title'] ?? '', 200),
        'description' => cp_text($challenge['description'] ?? '', 1000),
        'goalScope' => $scope,
        'goalLogic' => $logic,
        'startUtc' => cp_text($challenge['startUtc'] ?? '', 40),
        'endUtc' => isset($challenge['endUtc']) ? cp_text($challenge['endUtc'], 40) : null,
        'isCompleted' => (bool)($challenge['isCompleted'] ?? false),
        'percent' => cp_number($challenge['percent'] ?? 0, 0, 100),
        'reward' => cp_text($challenge['reward'] ?? '', 500),
        'goals' => $goals,
    ];
}

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') cp_reply(['ok' => false, 'error' => 'method_not_allowed'], 405);
cp_load_env(__DIR__ . '/../private/.env');
$expected = cp_env('CHALLENGE_PUSH_TOKEN');
if (strlen($expected) < 16) cp_reply(['ok' => false, 'error' => 'missing_server_push_token'], 500);
$given = cp_token();
if ($given === '' || !hash_equals($expected, $given)) cp_reply(['ok' => false, 'error' => 'forbidden'], 403);
if ((int)($_SERVER['CONTENT_LENGTH'] ?? 0) > 5 * 1024 * 1024) cp_reply(['ok' => false, 'error' => 'payload_too_large'], 413);
$payload = json_decode(file_get_contents('php://input') ?: '', true);
if (!is_array($payload) || !is_array($payload['challenges'] ?? null)) cp_reply(['ok' => false, 'error' => 'invalid_payload'], 400);
$challenges = [];
foreach (array_slice($payload['challenges'], 0, 100) as $challenge) if (is_array($challenge)) $challenges[] = cp_challenge($challenge);
$snapshot = ['schemaVersion' => 1, 'generatedAtUtc' => cp_text($payload['generatedAtUtc'] ?? gmdate('c'), 40), 'receivedAtUtc' => gmdate('c'), 'challenges' => $challenges];
$dir = cp_data_dir();
if (!is_dir($dir) && !mkdir($dir, 0750, true) && !is_dir($dir)) cp_reply(['ok' => false, 'error' => 'storage_unavailable'], 500);
$temp = tempnam($dir, 'challenge_');
if ($temp === false || file_put_contents($temp, json_encode($snapshot, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES), LOCK_EX) === false) cp_reply(['ok' => false, 'error' => 'write_failed'], 500);
$target = $dir . '/snapshot.json';
if (!rename($temp, $target)) { @unlink($temp); cp_reply(['ok' => false, 'error' => 'replace_failed'], 500); }
cp_reply(['ok' => true, 'receivedAtUtc' => $snapshot['receivedAtUtc'], 'challengeCount' => count($challenges)]);