<?php
declare(strict_types=1);

session_start();
header('Content-Type: application/json; charset=utf-8');

require_once __DIR__ . '/../functions/vote_function.php';

$steamId = $_SESSION['steamid'] ?? null;
if (!is_string($steamId) || $steamId === '') {
  echo json_encode(['status'=>'error','message'=>'Bitte einloggen.']);
  exit;
}

try {
  $state = vote_get_state($steamId);

  echo json_encode([
    'status' => 'ok',
    'votes_total' => $state['votes_total'],
    'votes_used' => $state['votes_used'],
    'votes_free' => $state['votes_free'],
    'next_claim_after' => $state['next_claim_after'],
    'voucher_balance' => vote_get_vouchers($steamId),
  ]);
} catch (Throwable $e) {
  echo json_encode(['status'=>'error','message'=>$e->getMessage()]);
}
