<?php
declare(strict_types=1);

require_once __DIR__ . '/env_function.php';

function discord_webhook_url(): string
{
    // ENV laden (nur einmal)
    static $envLoaded = false;
    if (!$envLoaded) {
        load_env(__DIR__ . '/../private/.env');
        $envLoaded = true;
    }

    $url = getenv('DISCORD_NEWS_CHANNEL');
    if (!is_string($url) || $url === '') {
        throw new RuntimeException('DISCORD_NEWS_CHANNEL fehlt in .env');
    }

    return $url;
}
