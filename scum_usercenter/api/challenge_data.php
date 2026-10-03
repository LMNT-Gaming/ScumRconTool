<?php
declare(strict_types=1);

if (session_status() !== PHP_SESSION_ACTIVE) session_start();
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: private, no-cache, must-revalidate');
header('Vary: Cookie, If-None-Match');

if (empty($_SESSION['steamid'])) { http_response_code(401); echo json_encode(['ok' => false, 'error' => 'login_required']); exit; }
$steamId = preg_replace('/\D/', '', (string)$_SESSION['steamid']) ?? '';
if ($steamId === '') { http_response_code(401); echo json_encode(['ok' => false, 'error' => 'login_required']); exit; }

require_once __DIR__ . '/../functions/env_function.php';
try { if (is_file(__DIR__ . '/../private/.env')) load_env(__DIR__ . '/../private/.env'); } catch (Throwable $e) {}
$configured = trim((string)(getenv('CHALLENGE_DATA_DIR') ?: ($_ENV['CHALLENGE_DATA_DIR'] ?? '')));
$file = ($configured !== '' ? rtrim($configured, '/\\') : __DIR__ . '/../private/challenge_data') . '/snapshot.json';
$snapshot = [];
if (is_file($file)) { $decoded = json_decode(file_get_contents($file) ?: '', true); if (is_array($decoded)) $snapshot = $decoded; }

$filtered = [];
foreach (is_array($snapshot['challenges'] ?? null) ? $snapshot['challenges'] : [] as $challenge) {
    if (!is_array($challenge)) continue;
    $personal = ($challenge['goalScope'] ?? '') === 'PerPlayer';
    $goals = [];
    $minePercents = [];
    $mineCompleted = [];
    foreach (is_array($challenge['goals'] ?? null) ? $challenge['goals'] : [] as $goal) {
        if (!is_array($goal)) continue;
        $target = max(1, (float)($goal['target'] ?? 1));
        $mine = null;
        foreach (is_array($goal['players'] ?? null) ? $goal['players'] : [] as $player) {
            if (is_array($player) && hash_equals($steamId, (string)($player['steamId'] ?? ''))) { $mine = $player; break; }
        }
        $mine = [
            'playerName' => (string)($mine['playerName'] ?? ''),
            'squadName' => (string)($mine['squadName'] ?? ''),
            'progress' => (float)($mine['progress'] ?? 0),
            'target' => (float)($mine['target'] ?? $target),
            'percent' => max(0, min(100, (float)($mine['percent'] ?? 0))),
            'isCompleted' => (bool)($mine['isCompleted'] ?? false),
        ];
        $publicGoal = ['key' => (string)($goal['key'] ?? ''), 'name' => (string)($goal['name'] ?? ''), 'target' => $target, 'mine' => $mine];
        if ($personal) {
            $publicGoal['progress'] = $mine['progress']; $publicGoal['percent'] = $mine['percent']; $publicGoal['isCompleted'] = $mine['isCompleted'];
        } else {
            $publicGoal['progress'] = (float)($goal['progress'] ?? 0); $publicGoal['percent'] = max(0, min(100, (float)($goal['percent'] ?? 0))); $publicGoal['isCompleted'] = (bool)($goal['isCompleted'] ?? false);
        }
        $goals[] = $publicGoal; $minePercents[] = $mine['percent']; $mineCompleted[] = $mine['isCompleted'];
    }
    $logicAll = ($challenge['goalLogic'] ?? '') === 'All';
    if ($personal) {
        $percent = !$minePercents ? 0 : ($logicAll ? array_sum($minePercents) / count($minePercents) : max($minePercents));
        $completed = !$mineCompleted ? false : ($logicAll ? !in_array(false, $mineCompleted, true) : in_array(true, $mineCompleted, true));
    } else { $percent = (float)($challenge['percent'] ?? 0); $completed = (bool)($challenge['isCompleted'] ?? false); }
    $filtered[] = [
        'id' => (string)($challenge['id'] ?? ''), 'type' => (string)($challenge['type'] ?? ''), 'title' => (string)($challenge['title'] ?? ''),
        'description' => (string)($challenge['description'] ?? ''), 'goalScope' => $personal ? 'PerPlayer' : 'Community',
        'goalLogic' => $logicAll ? 'All' : 'Any', 'startUtc' => $challenge['startUtc'] ?? null, 'endUtc' => $challenge['endUtc'] ?? null,
        'isCompleted' => $completed, 'percent' => max(0, min(100, $percent)), 'reward' => (string)($challenge['reward'] ?? ''), 'goals' => $goals,
    ];
}
$response = ['ok' => true, 'generatedAtUtc' => $snapshot['generatedAtUtc'] ?? null, 'receivedAtUtc' => $snapshot['receivedAtUtc'] ?? null, 'challenges' => $filtered];
$json = json_encode($response, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
$etag = '"' . hash('sha256', $steamId . '|' . $json) . '"';
header('ETag: ' . $etag);
if (trim((string)($_SERVER['HTTP_IF_NONE_MATCH'] ?? '')) === $etag) { http_response_code(304); exit; }
echo $json;