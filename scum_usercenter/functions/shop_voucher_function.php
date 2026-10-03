<?php
declare(strict_types=1);

require_once __DIR__ . '/db_function.php'; // muss db() -> PDO liefern

function shop_voucher_count_for_user(string $steamId): int
{
    $steamId = trim($steamId);
    if ($steamId === '') return 0;

    try {
        $stmt = db()->prepare("SELECT vouchers FROM user_vouchers WHERE steamid = :sid LIMIT 1");
        $stmt->execute([':sid' => $steamId]);
        $row = $stmt->fetch(PDO::FETCH_ASSOC);

        return (int)($row['vouchers'] ?? 0);
    } catch (Throwable $e) {
        // Header darf NIE sterben
        return 0;
    }
}
