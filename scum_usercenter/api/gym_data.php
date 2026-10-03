<?php
// api/gym_data.php
declare(strict_types=1);

if (session_status() !== PHP_SESSION_ACTIVE) session_start();
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');

if (empty($_SESSION['steamid'])) {
    http_response_code(401);
    echo json_encode(['ok' => false, 'error' => 'login_required'], JSON_UNESCAPED_UNICODE);
    exit;
}

require_once __DIR__ . '/../functions/env_function.php';
try {
    $env = __DIR__ . '/../private/.env';
    if (is_file($env)) load_env($env);
} catch (Throwable $e) {
    // Seite darf trotzdem antworten, nur optionale Env fehlt.
}

require_once __DIR__ . '/../functions/gym_data_function.php';

echo json_encode(gym_public_payload_for_steamid((string)$_SESSION['steamid']), JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
