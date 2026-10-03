<?php
declare(strict_types=1);

function insurance_env(string $key): string {
    return trim((string)(getenv($key) ?: ($_ENV[$key] ?? '')));
}
function insurance_load_config(): void {
    require_once __DIR__ . '/env_function.php';
    if (is_file(__DIR__ . '/../private/.env')) load_env(__DIR__ . '/../private/.env');
}
function insurance_dir(): string {
    return insurance_env('INSURANCE_DATA_DIR') ?: __DIR__ . '/../private/insurance_data';
}
function insurance_vehicles(string $steam): array {
    try {
        require_once __DIR__ . '/scum_user_function.php';
        $profile = scum_get_user_profile_by_steamid($steam);
        if (!scum_db_status()['ok']) throw new RuntimeException('SCUM database unavailable');
        if (!$profile || (int)($profile['id'] ?? 0) <= 0) return ['status'=>'profile_missing', 'items'=>[], 'databaseAtUtc'=>null];
        $vehicles = [];
        foreach (scum_get_locked_vehicles_by_user_profile_id((int)$profile['id']) as $vehicle) {
            $id = (string)($vehicle['id'] ?? '');
            if (!preg_match('/^[1-9]\d{0,18}$/', $id)) continue;
            $vehicles[$id] = ['id'=>$id, 'name'=>(string)($vehicle['name'] ?? 'Fahrzeug'), 'lastAccess'=>(string)($vehicle['last_access'] ?? 'Unbekannt')];
        }
        $items = array_values($vehicles);
        usort($items, static fn(array $a,array $b): int => strnatcasecmp($a['name'], $b['name']) ?: strcmp($a['id'], $b['id']));
        $file = __DIR__ . '/../scum_db/SCUM.db';
        $modified = is_file($file) ? filemtime($file) : false;
        return ['status'=>'ok', 'items'=>$items, 'databaseAtUtc'=>$modified === false ? null : gmdate('c', $modified)];
    } catch (Throwable $e) {
        error_log('Insurance vehicles: '.$e->getMessage());
        return ['status'=>'unavailable', 'items'=>null, 'databaseAtUtc'=>null];
    }
}
function insurance_reply(array $body, int $status = 200): never {
    http_response_code($status);
    echo json_encode($body, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}
function insurance_read(): array {
    $file = insurance_dir() . '/snapshot.json';
    if (!is_file($file)) return [];
    $value = json_decode((string)file_get_contents($file), true, 32, JSON_THROW_ON_ERROR | JSON_BIGINT_AS_STRING);
    if (!is_array($value)) throw new RuntimeException('Invalid insurance snapshot');
    return $value;
}
function insurance_text(mixed $value, int $max): string {
    if (!is_scalar($value)) throw new InvalidArgumentException('Invalid text');
    $text = trim((string)$value);
    if (strlen($text) > $max) throw new InvalidArgumentException('Text too long');
    return $text;
}
function insurance_date(mixed $value): ?string {
    if ($value === null) return null;
    if (!is_string($value) || strlen($value) > 40 || !preg_match('/^\d{4}-\d{2}-\d{2}T/', $value)) throw new InvalidArgumentException('Invalid date');
    return (new DateTimeImmutable($value))->setTimezone(new DateTimeZone('UTC'))->format('c');
}
function insurance_policy(array $p): array {
    if (!preg_match('/^V-[A-F0-9]{12}$/', (string)($p['id'] ?? '')) || !preg_match('/^\d{17}$/', (string)($p['steamId'] ?? '')) || !preg_match('/^[1-9]\d{0,18}$/', (string)($p['vehicleId'] ?? '')))
        throw new InvalidArgumentException('Invalid policy identity');
    if (!in_array($p['status'] ?? '', ['PaymentReview','Active','DestroyedPendingCheck','Claimable','SpawnReview','Claimed','Cancelled'], true)) throw new InvalidArgumentException('Invalid status');
    if (!in_array($p['weeks'] ?? 0, [1,2,4], true) || !is_int($p['price'] ?? null) || $p['price'] < 1 || $p['price'] > 400000000) throw new InvalidArgumentException('Invalid price or duration');
    $start = insurance_date($p['startUtc'] ?? null); $end = insurance_date($p['endUtc'] ?? null);
    if ($start === null || $end === null || $end <= $start) throw new InvalidArgumentException('Invalid coverage period');
    return [
        'id'=>$p['id'], 'steamId'=>$p['steamId'], 'vehicleId'=>(string)$p['vehicleId'],
        'playerName'=>insurance_text($p['playerName'] ?? '',100), 'vehicleName'=>insurance_text($p['vehicleName'] ?? '',100),
        'spawnClass'=>insurance_text($p['spawnClass'] ?? '',100), 'price'=>$p['price'], 'weeks'=>$p['weeks'],
        'startUtc'=>$start, 'endUtc'=>$end, 'destroyedUtc'=>insurance_date($p['destroyedUtc'] ?? null),
        'claimedUtc'=>insurance_date($p['claimedUtc'] ?? null), 'status'=>$p['status']
    ];
}
