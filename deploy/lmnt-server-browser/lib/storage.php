<?php
declare(strict_types=1);

function lmnt_config(): array
{
    static $config;
    if ($config === null) {
        $config = require dirname(__DIR__) . '/config.php';
    }
    return $config;
}

function lmnt_store(callable $callback)
{
    $config = lmnt_config();
    $path = $config['data_file'];
    $directory = dirname($path);
    if (!is_dir($directory) && !mkdir($directory, 0750, true) && !is_dir($directory)) {
        throw new RuntimeException('Data directory cannot be created.');
    }

    $handle = fopen($path, 'c+');
    if ($handle === false) {
        throw new RuntimeException('Data file cannot be opened.');
    }

    try {
        if (!flock($handle, LOCK_EX)) {
            throw new RuntimeException('Data file cannot be locked.');
        }
        rewind($handle);
        $raw = stream_get_contents($handle);
        $data = is_string($raw) && trim($raw) !== '' ? json_decode($raw, true) : [];
        if (!is_array($data)) $data = [];
        if (!isset($data['servers']) || !is_array($data['servers'])) $data['servers'] = [];
        if (!isset($data['registrations']) || !is_array($data['registrations'])) $data['registrations'] = [];

        $result = $callback($data);
        rewind($handle);
        ftruncate($handle, 0);
        fwrite($handle, json_encode($data, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE));
        fflush($handle);
        flock($handle, LOCK_UN);
        return $result;
    } finally {
        fclose($handle);
    }
}

function lmnt_read_servers(): array
{
    return lmnt_store(function (array &$data): array {
        lmnt_prune($data);
        return array_values($data['servers']);
    });
}

function lmnt_prune(array &$data): void
{
    $config = lmnt_config();
    $now = time();
    $retain = (int)$config['retain_after_seconds'];
    foreach ($data['servers'] as $id => $server) {
        if (!is_array($server) || (int)($server['last_seen'] ?? 0) < $now - $retain) {
            unset($data['servers'][$id]);
        }
    }
    foreach ($data['registrations'] as $key => $timestamps) {
        if (!is_array($timestamps)) {
            unset($data['registrations'][$key]);
            continue;
        }
        $timestamps = array_values(array_filter($timestamps, static function ($value) use ($now): bool {
            return is_int($value) && $value >= $now - 86400;
        }));
        if ($timestamps === []) unset($data['registrations'][$key]);
        else $data['registrations'][$key] = $timestamps;
    }
}

function lmnt_token_hash(string $token): string
{
    return hash_hmac('sha256', $token, (string)lmnt_config()['token_pepper']);
}
