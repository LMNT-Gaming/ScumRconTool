<?php
declare(strict_types=1);

// Copy to config.php and generate two independent random secrets for your installation.
return [
    'data_file' => __DIR__ . '/data/servers.json',
    'registration_limit_per_day' => 10,
    'online_after_seconds' => 1800,
    'retain_after_seconds' => 7776000,
    'rate_limit_salt' => 'REPLACE_WITH_RANDOM_SECRET',
    'token_pepper' => 'REPLACE_WITH_ANOTHER_RANDOM_SECRET',
];
