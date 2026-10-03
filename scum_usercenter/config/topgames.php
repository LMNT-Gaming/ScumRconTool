<?php
declare(strict_types=1);

require_once __DIR__ . '/../functions/env_function.php';

function topgames_config(): array
{
    static $cfg = null;
    if (is_array($cfg)) return $cfg;

    // ENV laden (nur einmal)
    static $envLoaded = false;
    if (!$envLoaded) {
        load_env(__DIR__ . '/../private/.env');
        $envLoaded = true;
    }

    $apiBase = getenv('TOPGAMES_API_BASE') ?: 'https://api.top-games.net/v1';
    $token   = getenv('TOPGAMES_SERVER_TOKEN');
    $voucher = (int)(getenv('VOTE_VOUCHERS_PER_VOTE') ?: 1);

    if (!$token) {
        throw new RuntimeException('TOPGAMES_SERVER_TOKEN fehlt in .env');
    }

    $cfg = [
        'api_base' => $apiBase,
        'token'    => $token,
        'vouchers' => $voucher,
    ];

    return $cfg;
}
