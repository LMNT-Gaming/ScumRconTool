<?php
// functions/db_function.php
declare(strict_types=1);

require_once __DIR__ . '/env_function.php';

/**
 * Zentrale MySQL DB Verbindung
 */
function db(): PDO
{
    static $pdo = null;
    if ($pdo instanceof PDO) {
        return $pdo;
    }

    // ENV laden (nur einmal)
    static $envLoaded = false;
    if (!$envLoaded) {
        load_env(__DIR__ . '/../private/.env');
        $envLoaded = true;
    }

    $host    = getenv('MYSQL_HOST');
    $db      = getenv('MYSQL_DB');
    $user    = getenv('MYSQL_USER');
    $pass    = getenv('MYSQL_PASS');
    $charset = getenv('MYSQL_CHARSET') ?: 'utf8mb4';

    if (!$host || !$db || !$user) {
        throw new RuntimeException('MySQL ENV Variablen fehlen');
    }

    $dsn = sprintf(
        'mysql:host=%s;dbname=%s;charset=%s',
        $host,
        $db,
        $charset
    );

    $options = [
        PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES   => false,
    ];

    $pdo = new PDO($dsn, $user, $pass, $options);
    return $pdo;
}
