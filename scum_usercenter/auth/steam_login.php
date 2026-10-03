<?php

declare(strict_types=1);

session_start();

require_once __DIR__ . '/../includes/config.php';

function getSteamIDFromClaimedId(string $claimedId)
{
    if (preg_match('~^https?://steamcommunity\.com/openid/id/(\d{17,25})$~', $claimedId, $m)) {
        return $m[1];
    }
    return false;
}

function addQueryParam(string $url, string $key, string $value): string
{
    return $url . (strpos($url, '?') !== false ? '&' : '?') . rawurlencode($key) . '=' . rawurlencode($value);
}
function buildReturnToUrl(): string
{
    return 'https://lmnt-gaming.net/scum/auth/steam_login.php';
}

/**
 * Steam OpenID: Redirect zu Steam
 */
if (!isset($_GET['openid_mode'])) {

    // Nonce/State gegen "fremde Rückleitung" (CSRF-artig)
    $_SESSION['steam_openid_state'] = bin2hex(random_bytes(16));
    $_SESSION['steam_openid_ts']    = time();
    $realm = 'https://lmnt-gaming.net'; // NUR Domain
    $returnTo = addQueryParam(
        'https://lmnt-gaming.net/scum/auth/steam_login.php', // dein Callback
        'state',
        $_SESSION['steam_openid_state']
    );




    $params = [
        'openid.ns'         => 'http://specs.openid.net/auth/2.0',
        'openid.mode'       => 'checkid_setup',
        'openid.return_to'  => $returnTo,
        'openid.realm'      => $realm,
        'openid.identity'   => 'http://specs.openid.net/auth/2.0/identifier_select',
        'openid.claimed_id' => 'http://specs.openid.net/auth/2.0/identifier_select',
    ];

    header('Location: ' . STEAM_OPENID . '?' . http_build_query($params));
    exit;
}

/**
 * Abbruch
 */
if (($_GET['openid_mode'] ?? '') === 'cancel') {
    header('Location: ' . (APP_BASE_URL . '/'));
    exit;
}

/**
 * Rückkehr von Steam: Validieren
 */
if (($_GET['openid_mode'] ?? '') === 'id_res') {

    // State prüfen
    $state = (string)($_GET['state'] ?? '');
    if (!$state || empty($_SESSION['steam_openid_state']) || !hash_equals($_SESSION['steam_openid_state'], $state)) {
        http_response_code(400);
        exit('Ungültiger Login-State.');
    }

    // optional: State-Timeout (z.B. 10 Minuten)
    $ts = (int)($_SESSION['steam_openid_ts'] ?? 0);
    if ($ts && (time() - $ts) > 600) {
        http_response_code(400);
        exit('Login-Request abgelaufen. Bitte erneut versuchen.');
    }

    $signed = explode(',', (string)($_GET['openid_signed'] ?? ''));

    $params = [
        'openid.assoc_handle' => (string)($_GET['openid_assoc_handle'] ?? ''),
        'openid.signed'       => (string)($_GET['openid_signed'] ?? ''),
        'openid.sig'          => (string)($_GET['openid_sig'] ?? ''),
        'openid.ns'           => 'http://specs.openid.net/auth/2.0',
        'openid.mode'         => 'check_authentication',
    ];

    foreach ($signed as $item) {
        $key = 'openid_' . str_replace('.', '_', $item);
        $val = (string)($_GET[$key] ?? '');
        $params['openid.' . $item] = $val;
    }

    $opts = [
        'http' => [
            'method'  => 'POST',
            'header'  => "Content-Type: application/x-www-form-urlencoded\r\n",
            'content' => http_build_query($params),
            'timeout' => 10,
        ],
    ];

    $context = stream_context_create($opts);
    $result  = @file_get_contents(STEAM_OPENID, false, $context);

    // Debug optional (nur wenn du willst)
    // file_put_contents(__DIR__ . '/steam_debug.txt', $result . "\n\n" . print_r($_GET, true), FILE_APPEND);

    if ($result !== false && preg_match('/is_valid\s*:\s*true/i', $result)) {
        $steamID = getSteamIDFromClaimedId((string)($_GET['openid_claimed_id'] ?? ''));
        if ($steamID !== false) {

            // Session-Fixation vermeiden
            session_regenerate_id(true);

            $_SESSION['steamid'] = $steamID;
            unset($_SESSION['isAdminChecked'], $_SESSION['isAdmin']);

            // State aufräumen
            unset($_SESSION['steam_openid_state'], $_SESSION['steam_openid_ts']);

            $next = $_SESSION['login_next'] ?? APP_LOGIN_REDIRECT;
            unset($_SESSION['login_next']);

            header('Location: ' . $next);
            exit;
        }
    }

    http_response_code(401);
    exit('Steam Login fehlgeschlagen.');
}

http_response_code(400);
exit('Unbekannter Fehler.');
