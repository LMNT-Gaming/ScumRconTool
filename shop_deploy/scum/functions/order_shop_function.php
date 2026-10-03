<?php
declare(strict_types=1);

require_once __DIR__ . '/db_function.php';
require_once __DIR__ . '/env_function.php';

function order_shop_env(string $key, string $default = ''): string
{
    static $loaded = false;
    if (!$loaded) {
        load_env(__DIR__ . '/../private/.env');
        $loaded = true;
    }
    $value = getenv($key);
    return $value === false || trim((string)$value) === '' ? $default : trim((string)$value);
}

function order_shop_schema(): void
{
    static $ready = false;
    if ($ready) return;
    $pdo = db();
    $pdo->exec("CREATE TABLE IF NOT EXISTS rr_shop_packs (
        id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        category VARCHAR(40) NOT NULL,
        name VARCHAR(120) NOT NULL,
        description TEXT NOT NULL,
        image_url VARCHAR(500) NOT NULL DEFAULT '',
        price INT UNSIGNED NOT NULL,
        fulfillment_type VARCHAR(20) NOT NULL DEFAULT 'items',
        payload_json LONGTEXT NOT NULL,
        enabled TINYINT(1) NOT NULL DEFAULT 1,
        sort_order INT NOT NULL DEFAULT 0,
        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        KEY idx_rr_shop_packs_visible (enabled, category, sort_order)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
    $pdo->exec("CREATE TABLE IF NOT EXISTS rr_shop_orders (
        id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
        code VARCHAR(20) NOT NULL,
        steam_id VARCHAR(20) NOT NULL,
        pack_id BIGINT UNSIGNED NULL,
        pack_name VARCHAR(120) NOT NULL,
        price INT UNSIGNED NOT NULL,
        order_json LONGTEXT NOT NULL,
        status VARCHAR(32) NOT NULL DEFAULT 'pending',
        lease_token CHAR(64) NULL,
        details VARCHAR(1000) NOT NULL DEFAULT '',
        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        expires_at DATETIME NOT NULL,
        processed_at DATETIME NULL,
        completed_at DATETIME NULL,
        UNIQUE KEY uq_rr_shop_orders_code (code),
        KEY idx_rr_shop_orders_player (steam_id, status, expires_at)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
    $ready = true;
}

function order_shop_categories(): array
{
    return [
        'mechanic' => ['name' => 'Redline Motors', 'subtitle' => 'Fahrzeuge, Ersatzteile und Werkstattbedarf'],
        'construction' => ['name' => 'Iron Nest Construction', 'subtitle' => 'Material und Packs für deinen Basenbau'],
        'armorer' => ['name' => 'Blackwing Armory', 'subtitle' => 'Waffen, Magazine und Munition'],
        'medic' => ['name' => 'RavenCare Medical', 'subtitle' => 'Medizinische Versorgung für die Insel'],
    ];
}

function order_shop_packs(bool $admin = false): array
{
    order_shop_schema();
    $where = $admin ? '' : 'WHERE enabled = 1';
    return db()->query("SELECT * FROM rr_shop_packs $where ORDER BY category, sort_order, name")?->fetchAll() ?: [];
}

function order_shop_pack(int $id): ?array
{
    order_shop_schema();
    $stmt = db()->prepare('SELECT * FROM rr_shop_packs WHERE id=:id LIMIT 1');
    $stmt->execute([':id' => $id]);
    $row = $stmt->fetch();
    return is_array($row) ? $row : null;
}

function order_shop_parse_payload(string $type, string $lines): array
{
    $type = $type === 'vehicle' ? 'vehicle' : 'items';
    $rows = preg_split('/\R/', trim($lines)) ?: [];
    $payload = [];
    foreach ($rows as $line) {
        $line = trim($line);
        if ($line === '') continue;
        $parts = array_map('trim', explode('|', $line));
        $code = preg_replace('/[^A-Za-z0-9_\-]/', '', (string)($parts[0] ?? '')) ?: '';
        $quantity = max(1, min(100, (int)($parts[1] ?? 1)));
        if ($code === '') throw new DomainException('Ein Spawn-Code ist ungültig. Erlaubt sind Buchstaben, Zahlen, _ und -.');
        $payload[] = ['code' => $code, 'quantity' => $quantity];
    }
    if (!$payload) throw new DomainException('Bitte mindestens einen Spawn-Code eintragen. Format: SpawnCode|Menge');
    if ($type === 'vehicle' && (count($payload) !== 1 || $payload[0]['quantity'] !== 1)) {
        throw new DomainException('Ein Fahrzeugpack muss exakt ein Fahrzeug mit Menge 1 enthalten.');
    }
    return $payload;
}

function order_shop_payload_lines(array $pack): string
{
    $payload = json_decode((string)($pack['payload_json'] ?? '[]'), true);
    if (!is_array($payload)) return '';
    return implode("\n", array_map(static fn(array $row): string => (string)($row['code'] ?? '') . '|' . max(1, (int)($row['quantity'] ?? 1)), $payload));
}

function order_shop_save_pack(array $post): void
{
    order_shop_schema();
    $categories = order_shop_categories();
    $id = max(0, (int)($post['pack_id'] ?? 0));
    $category = (string)($post['category'] ?? '');
    $name = trim((string)($post['name'] ?? ''));
    $type = (string)($post['fulfillment_type'] ?? 'items') === 'vehicle' ? 'vehicle' : 'items';
    $price = (int)($post['price'] ?? 0);
    if (!isset($categories[$category])) throw new DomainException('Unbekannter Untershop.');
    if ($name === '' || strlen($name) > 240) throw new DomainException('Bitte einen kurzen Packnamen angeben.');
    if ($price < 1 || $price > 100000000) throw new DomainException('Der Preis muss zwischen 1 und 100.000.000 Scummies liegen.');
    $payload = order_shop_parse_payload($type, (string)($post['payload_lines'] ?? ''));
    $values = [
        ':category' => $category, ':name' => $name,
        ':description' => trim((string)($post['description'] ?? '')),
        ':image_url' => trim((string)($post['image_url'] ?? '')),
        ':price' => $price, ':type' => $type,
        ':payload' => json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE),
        ':enabled' => empty($post['enabled']) ? 0 : 1,
        ':sort_order' => (int)($post['sort_order'] ?? 0),
    ];
    if ($id > 0) {
        $values[':id'] = $id;
        $stmt = db()->prepare('UPDATE rr_shop_packs SET category=:category,name=:name,description=:description,image_url=:image_url,price=:price,fulfillment_type=:type,payload_json=:payload,enabled=:enabled,sort_order=:sort_order WHERE id=:id');
    } else {
        $stmt = db()->prepare('INSERT INTO rr_shop_packs (category,name,description,image_url,price,fulfillment_type,payload_json,enabled,sort_order) VALUES (:category,:name,:description,:image_url,:price,:type,:payload,:enabled,:sort_order)');
    }
    $stmt->execute($values);
}

function order_shop_delete_pack(int $id): void
{
    order_shop_schema();
    $stmt = db()->prepare('DELETE FROM rr_shop_packs WHERE id=:id AND NOT EXISTS (SELECT 1 FROM rr_shop_orders WHERE pack_id=:id2 AND status IN (\'pending\',\'processing\',\'review\'))');
    $stmt->execute([':id' => $id, ':id2' => $id]);
    if ($stmt->rowCount() !== 1) throw new DomainException('Pack mit offenen Bestellungen kann nicht gelöscht werden. Deaktiviere es stattdessen.');
}

function order_shop_csrf(): string
{
    if (empty($_SESSION['order_shop_csrf'])) $_SESSION['order_shop_csrf'] = bin2hex(random_bytes(24));
    return (string)$_SESSION['order_shop_csrf'];
}

function order_shop_verify_csrf(string $token): void
{
    if ($token === '' || !hash_equals(order_shop_csrf(), $token)) throw new DomainException('Sitzung abgelaufen. Bitte lade die Seite neu.');
}

function order_shop_http_player(string $steamId): array
{
    $base = rtrim(order_shop_env('GGCON_BASE_URL'), '/');
    $password = order_shop_env('GGCON_PASSWORD');
    if ($base === '' || $password === '') throw new RuntimeException('Der Shop ist noch nicht mit ggCON verbunden.');
    $ch = curl_init($base . '/players/' . rawurlencode($steamId) . '.json');
    curl_setopt_array($ch, [CURLOPT_RETURNTRANSFER => true, CURLOPT_CONNECTTIMEOUT => 4, CURLOPT_TIMEOUT => 10,
        CURLOPT_HTTPHEADER => ['Accept: application/json', 'X-Password: ' . $password], CURLOPT_SSL_VERIFYPEER => true]);
    $raw = curl_exec($ch); $http = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE); $error = curl_error($ch); curl_close($ch);
    if ($raw === false) throw new RuntimeException('ggCON ist derzeit nicht erreichbar: ' . $error);
    $data = json_decode((string)$raw, true);
    if (!is_array($data) || $http < 200 || $http >= 300 || empty($data['ok'])) {
        throw new DomainException('Dein aktueller Kontostand konnte nicht geprüft werden. Du musst dafür auf dem Server online sein.');
    }
    $player = isset($data['player']) && is_array($data['player']) ? $data['player'] : $data;
    return $player;
}

function order_shop_create_order(string $steamId, int $packId): array
{
    order_shop_schema();
    if (strlen(order_shop_env('SHOP_WORKER_TOKEN')) < 24) throw new RuntimeException('Der Bestellservice ist noch nicht vollständig eingerichtet.');
    $steamId = preg_replace('/\D/', '', $steamId) ?: '';
    if (strlen($steamId) !== 17) throw new DomainException('Ungültige SteamID.');
    $pack = order_shop_pack($packId);
    if (!$pack || empty($pack['enabled'])) throw new DomainException('Dieses Pack ist nicht mehr verfügbar.');
    $price = (int)$pack['price'];
    $player = order_shop_http_player($steamId);
    $balance = isset($player['accountBalance']) ? (float)$player['accountBalance'] : -1;
    if ($balance < $price) throw new DomainException('Nicht genügend Scummies. Benötigt: ' . number_format($price, 0, ',', '.') . '$.');
    $open = db()->prepare("SELECT COUNT(*) FROM rr_shop_orders WHERE steam_id=:sid AND status IN ('pending','processing','review') AND expires_at>NOW()");
    $open->execute([':sid' => $steamId]);
    if ((int)$open->fetchColumn() >= 8) throw new DomainException('Du hast bereits zu viele offene Bestellungen. Löse zuerst einen Code im Spiel ein.');
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    do {
        $raw = random_bytes(9); $suffix = '';
        for ($i = 0; $i < 9; $i++) $suffix .= $alphabet[ord($raw[$i]) % strlen($alphabet)];
        $code = 'RR-' . $suffix;
        $exists = db()->prepare('SELECT 1 FROM rr_shop_orders WHERE code=:code'); $exists->execute([':code' => $code]);
    } while ($exists->fetchColumn());
    $snapshot = ['type' => (string)$pack['fulfillment_type'], 'entries' => json_decode((string)$pack['payload_json'], true) ?: []];
    $stmt = db()->prepare("INSERT INTO rr_shop_orders (code,steam_id,pack_id,pack_name,price,order_json,status,expires_at) VALUES (:code,:sid,:pack,:name,:price,:json,'pending',DATE_ADD(UTC_TIMESTAMP(), INTERVAL 24 HOUR))");
    $stmt->execute([':code'=>$code, ':sid'=>$steamId, ':pack'=>$packId, ':name'=>$pack['name'], ':price'=>$price,
        ':json'=>json_encode($snapshot, JSON_UNESCAPED_SLASHES|JSON_UNESCAPED_UNICODE)]);
    return ['code'=>$code, 'name'=>(string)$pack['name'], 'price'=>$price, 'expires'=>'24 Stunden'];
}

function order_shop_orders_for_player(string $steamId): array
{
    order_shop_schema();
    $stmt = db()->prepare("SELECT code,pack_name,price,status,created_at,expires_at FROM rr_shop_orders WHERE steam_id=:sid AND status IN ('pending','processing','review','completed') ORDER BY id DESC LIMIT 20");
    $stmt->execute([':sid'=>$steamId]);
    return $stmt->fetchAll() ?: [];
}

function order_shop_admin_orders(): array
{
    order_shop_schema();
    return db()->query("SELECT id,code,steam_id,pack_name,price,status,details,created_at,processed_at,completed_at FROM rr_shop_orders ORDER BY id DESC LIMIT 100")?->fetchAll() ?: [];
}

function order_shop_admin_resolve(int $id, string $status): void
{
    if (!in_array($status, ['pending','completed','cancelled'], true)) throw new DomainException('Ungültiger Zielstatus.');
    order_shop_schema();
    $stmt = db()->prepare("UPDATE rr_shop_orders SET status=:status,lease_token=NULL,details=CONCAT(details, :note),completed_at=IF(:completed='completed',UTC_TIMESTAMP(),completed_at) WHERE id=:id AND status IN ('processing','review')");
    $stmt->execute([':status'=>$status, ':note'=>' | Manually resolved in Admincenter.', ':completed'=>$status, ':id'=>$id]);
    if ($stmt->rowCount() !== 1) throw new DomainException('Nur gesperrte Bestellungen können manuell bearbeitet werden.');
}
