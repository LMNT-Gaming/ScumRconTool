<?php
// /functions/vote_function.php
declare(strict_types=1);

require_once __DIR__ . '/db_function.php';
require_once __DIR__ . '/env_function.php';

load_env(__DIR__ . '/../private/.env');
function env_int(string $key, int $default): int
{
    $v = getenv($key);
    return ($v === false || $v === '') ? $default : (int)$v;
}
function env_str(string $key, string $default = ''): string
{
    $v = getenv($key);
    return ($v === false) ? $default : (string)$v;
}

function vote_pdo(): PDO
{
    return db();
}

function vote_get_state(string $steamId): array
{
    $pdo = vote_pdo();
    $st = $pdo->prepare("SELECT votes, usedvotes, next_claim_after, playername FROM vote_players WHERE steamid = :sid LIMIT 1");
    $st->execute([':sid' => $steamId]);
    $row = $st->fetch(PDO::FETCH_ASSOC) ?: [];

    $votes = (int)($row['votes'] ?? 0);
    $used  = (int)($row['usedvotes'] ?? 0);

    return [
        'votes_total' => $votes,
        'votes_used' => $used,
        'votes_free' => max(0, $votes - $used),
        'next_claim_after' => isset($row['next_claim_after']) ? (string)$row['next_claim_after'] : null,
        'playername' => isset($row['playername']) ? (string)$row['playername'] : null,
    ];
}

function vote_get_vouchers(string $steamId): int
{
    $pdo = vote_pdo();
    $st = $pdo->prepare("SELECT vouchers FROM user_vouchers WHERE steamid = :sid LIMIT 1");
    $st->execute([':sid' => $steamId]);
    $row = $st->fetch(PDO::FETCH_ASSOC);
    return (int)($row['vouchers'] ?? 0);
}

function vote_add_vouchers(string $steamId, int $amount): void
{
    if ($amount <= 0) return;
    $pdo = vote_pdo();
    $pdo->prepare("
        INSERT INTO user_vouchers (steamid, vouchers)
        VALUES (:sid, :v)
        ON DUPLICATE KEY UPDATE vouchers = vouchers + VALUES(vouchers)
    ")->execute([':sid' => $steamId, ':v' => $amount]);
}

function vote_is_on_cooldown(?string $nextClaimAfter): bool
{
    if (!$nextClaimAfter) return false;
    $ts = strtotime($nextClaimAfter);
    if (!$ts) return false;
    return time() < $ts;
}

function vote_topgames_check(string $playerName): array
{
    $apiBase = rtrim(env_str('TOPGAMES_API_BASE', 'https://api.top-games.net/v1'), '/');
    $token   = env_str('TOPGAMES_SERVER_TOKEN', '');

    if ($token === '') {
        return ['ok' => false, 'error' => 'TOPGAMES_SERVER_TOKEN fehlt in .env', 'http' => 0, 'raw' => null];
    }

    $url = $apiBase . '/votes/check?server_token='
        . urlencode($token)
        . '&playername=' . urlencode($playerName);


    $ch = curl_init($url);
    curl_setopt_array($ch, [
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_TIMEOUT        => 12,
        CURLOPT_SSL_VERIFYPEER => true,
    ]);

    $body = curl_exec($ch);
    $http = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE);
    $err  = curl_error($ch);
    curl_close($ch);

    if ($body === false) {
        return ['ok' => false, 'error' => 'curl: ' . $err, 'http' => $http, 'raw' => null];
    }

    $json = json_decode($body, true);
    if (!is_array($json)) {
        return ['ok' => false, 'error' => 'invalid json', 'http' => $http, 'raw' => substr($body, 0, 200)];
    }

    // 404 => kein Vote in letzter Zeit
    if ($http === 404 || (($json['code'] ?? null) === 404)) {
        return ['ok' => false, 'no_vote' => true, 'http' => $http, 'json' => $json];
    }

    if ($http !== 200 || empty($json['success'])) {
        return ['ok' => false, 'error' => 'api error', 'http' => $http, 'json' => $json];
    }

    $durationMin = (float)($json['duration'] ?? 0);
    if ($durationMin <= 0) {
        return ['ok' => false, 'error' => 'duration invalid', 'http' => $http, 'json' => $json];
    }

    return ['ok' => true, 'duration_minutes' => $durationMin, 'json' => $json];
}

function vote_claim_and_reward(string $steamId, string $playerName): array
{
    $pdo = vote_pdo();
$vouchersPerClaim = env_int('VOTE_VOUCHERS_PER_CLAIM', 1);
    // Cooldown aus DB
    $state = vote_get_state($steamId);
    if (vote_is_on_cooldown($state['next_claim_after'])) {
        return [
            'status' => 'cooldown',
            'message' => 'Du hast deinen Vote schon geclaimed. Bitte warte bis zum nächsten Claim.',
            'next_claim_after' => $state['next_claim_after'],
            'voucher_balance' => vote_get_vouchers($steamId),
        ];
    }

    // Top-Games prüfen
    $check = vote_topgames_check($playerName);
    if (!empty($check['no_vote'])) {
        $msg = $check['json']['message'] ?? 'Kein Vote gefunden (letzte 2h).';
        return [
            'status' => 'no_vote',
            'message' => $msg,
            'voucher_balance' => vote_get_vouchers($steamId),
        ];
    }
    if (empty($check['ok'])) {
        $msg = $check['error'] ?? 'Top-Games Fehler.';
        return [
            'status' => 'api_error',
            'message' => $msg,
            'voucher_balance' => vote_get_vouchers($steamId),
        ];
    }

    // next_claim_after = jetzt + durationMin
    $durationMin = (float)$check['duration_minutes'];
    $nextTs = time() + (int)ceil($durationMin * 60);
    $nextClaimAfter = date('Y-m-d H:i:s', $nextTs);

    $pdo->beginTransaction();
    try {
        // Vote hochzählen und Cooldown setzen
        $pdo->prepare("
            INSERT INTO vote_players (steamid, playername, votes, usedvotes, next_claim_after)
            VALUES (:sid, :pn, 1, 0, :nca)
            ON DUPLICATE KEY UPDATE
              playername = VALUES(playername),
              votes = votes + 1,
              next_claim_after = VALUES(next_claim_after)
        ")->execute([
            ':sid' => $steamId,
            ':pn'  => $playerName,
            ':nca' => $nextClaimAfter,
        ]);

        // Gutscheine gutschreiben
        vote_add_vouchers($steamId, $vouchersPerClaim);


        // log
        $pdo->prepare("
            INSERT INTO vote_claim_log (steamid, playername, vouchers_added)
            VALUES (:sid, :pn, :va)
        ")->execute([
            ':sid' => $steamId,
            ':pn'  => $playerName,
            ':va' => $vouchersPerClaim,
        ]);

        $pdo->commit();
    } catch (Throwable $e) {
        $pdo->rollBack();
        return ['status' => 'error', 'message' => 'DB Fehler beim Claim: ' . $e->getMessage()];
    }

    $newState = vote_get_state($steamId);

    return [
        'status' => 'rewarded',
        'message' => 'Danke für deinen Vote! +' . $vouchersPerClaim . ' Gutschein(e) gutgeschrieben.',
        'votes_total' => $newState['votes_total'],
        'votes_used' => $newState['votes_used'],
        'votes_free' => $newState['votes_free'],
        'next_claim_after' => $nextClaimAfter,
        'voucher_balance' => vote_get_vouchers($steamId),
    ];
}
