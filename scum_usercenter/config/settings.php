<?php
//settings.php
declare(strict_types=1);

require_once __DIR__ . '/../functions/env_function.php';

function shop_discord_webhook(): string
{
    // ENV nur einmal laden
    static $envLoaded = false;
    if (!$envLoaded) {
        load_env(__DIR__ . '/../private/.env');
        $envLoaded = true;
    }

    $url = getenv('DISCORD_SHOP_WEBHOOK');
    if (!is_string($url) || $url === '') {
        return '';
    }
    return $url;
}
