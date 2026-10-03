<?php
declare(strict_types=1);

session_start();
header('Content-Type: application/json; charset=utf-8');

require_once __DIR__ . '/../functions/vote_function.php';
require_once __DIR__ . '/../functions/scum_user_function.php';

$steamId = $_SESSION['steamid'] ?? null;
if (!is_string($steamId) || $steamId === '') {
  echo json_encode(['status'=>'error','message'=>'Bitte einloggen.']);
  exit;
}

// Playername: nimm den SCUM Namen (ohne SteamID Anzeige)
$userProfile = scum_get_user_profile_by_steamid($steamId);
$playerName = (string)($userProfile['name'] ?? '');
if ($playerName === '') $playerName = 'Unbekannt';

try {
  $res = vote_claim_and_reward($steamId, $playerName);
  echo json_encode($res);
} catch (Throwable $e) {
  echo json_encode(['status'=>'error','message'=>$e->getMessage()]);
}
