<?php
declare(strict_types=1);

header('Content-Type: application/json; charset=utf-8');
require_once __DIR__ . '/../functions/vote_function.php';

$secret = $_SERVER['HTTP_X_VOTE_SECRET'] ?? '';
$serverSecret = $_ENV['VOTE_WEBHOOK_SECRET'] ?? 'CHANGE_ME_SECRET';

if ($secret !== $serverSecret) {
  echo json_encode(['status' => 'error', 'message' => 'unauthorized']);
  exit;
}

$steamId = (string)($_POST['steamid'] ?? '');
$credits = (int)($_POST['credits'] ?? 0);

if ($steamId === '' || $credits <= 0) {
  echo json_encode(['status' => 'error', 'message' => 'missing steamid/credits']);
  exit;
}

vote_add_credits($steamId, $credits);

echo json_encode([
  'status' => 'ok',
  'credits_added' => $credits,
  'credits_now' => vote_get_credits($steamId),
]);
