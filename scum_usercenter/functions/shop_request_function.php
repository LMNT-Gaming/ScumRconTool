<?php
//shop_request_function.php
declare(strict_types=1);

require_once __DIR__ . '/db_function.php';

function shop_create_request(string $steamId, int $itemId, string $currency, int $price, ?int $x, ?int $y): int
{
    $stmt = db()->prepare("
      INSERT INTO shop_requests (steamid, item_id, currency, price, coord_x, coord_y, status)
      VALUES (:steamid, :item_id, :currency, :price, :x, :y, 'pending')
    ");
    $stmt->execute([
        ':steamid' => $steamId,
        ':item_id' => $itemId,
        ':currency' => $currency,
        ':price' => $price,
        ':x' => $x,
        ':y' => $y,
    ]);
    $id = (int)db()->lastInsertId();

    // Optional: Itemname für bessere Discord Nachricht laden
    $stmt = db()->prepare("SELECT name FROM shop_items WHERE id = ? LIMIT 1");
    $stmt->execute([$itemId]);
    $itemName = (string)($stmt->fetchColumn() ?: ('Item #' . $itemId));
    $buyerName = shop_get_player_name($steamId);
    shop_discord_notify('🧾 Neuer Shop-Kaufantrag', [
        'Request ID' => (string)$id,
        'Spieler'    => $buyerName,
        'Item'       => $itemName,
        'Currency'   => $currency,
        'Price'      => (string)$price,
        'Coords'     => ($x !== null && $y !== null) ? ($x . ', ' . $y) : '-',
        'Status'     => 'pending',
    ], 0xF59E0B);

    return $id;
}

function shop_list_requests(string $status = 'pending'): array
{
    $stmt = db()->prepare("
      SELECT r.*,
             i.name AS item_name,
             i.requires_coordinates
      FROM shop_requests r
      JOIN shop_items i ON i.id = r.item_id
      WHERE r.status = :status
      ORDER BY r.id DESC
    ");
    $stmt->execute([':status' => $status]);
    return $stmt->fetchAll();
}

function shop_get_request(int $id): ?array
{
    $stmt = db()->prepare("
      SELECT r.*,
             i.name AS item_name,
             i.requires_coordinates
      FROM shop_requests r
      JOIN shop_items i ON i.id = r.item_id
      WHERE r.id = ?
      LIMIT 1
    ");
    $stmt->execute([$id]);
    $row = $stmt->fetch();
    return $row ?: null;
}

function shop_update_request_status(int $id, string $newStatus, string $adminSteamId, string $note = ''): void
{
    $db = db();

    // Request laden (inkl. item_name)
    $stmt = $db->prepare("
        SELECT r.id, r.steamid, r.status, r.currency, r.price, r.voucher_charged,
               r.item_id,
               i.name AS item_name
        FROM shop_requests r
        JOIN shop_items i ON i.id = r.item_id
        WHERE r.id = ?
        LIMIT 1
    ");
    $stmt->execute([$id]);
    $req = $stmt->fetch(PDO::FETCH_ASSOC);
    if (!$req) return;

    $oldStatus = (string)$req['status'];

    $db->beginTransaction();
    try {

        $isApproveTransition = ($newStatus === 'approved' && $oldStatus !== 'approved');

        // Debug (siehst du danach in admin_note)
        $dbg = [];
        $dbg[] = "DBG old={$oldStatus} new={$newStatus} transition=" . ($isApproveTransition ? "1" : "0");
        $dbg[] = "DBG steamid=" . (string)$req['steamid'] . " item=" . (string)$req['item_name'];

        // Status updaten
        $stmt = $db->prepare("
        UPDATE shop_requests
        SET status = ?, admin_note = ?
        WHERE id = ?
    ");
        $stmt->execute([$newStatus, $note, $id]);

        // Inventar gutschreiben
        if ($isApproveTransition) {
            // WICHTIG: prüfen ob Tabelle wirklich in DER DB existiert
            $dbName = (string)$db->query("SELECT DATABASE()")->fetchColumn();
            $dbg[] = "DBG db=" . $dbName;

            $stmt = $db->prepare("
            INSERT INTO user_inventory (steamid, item_name, amount)
            VALUES (:steamid, :item_name, 1)
            ON DUPLICATE KEY UPDATE amount = amount + 1
        ");
            $stmt->execute([
                ':steamid'   => (string)$req['steamid'],
                ':item_name' => (string)$req['item_name'],
            ]);

            $dbg[] = "DBG inv_rowcount=" . (string)$stmt->rowCount();
            error_log("SHOP_INV: " . implode(" | ", $dbg));
        } else {
            error_log("SHOP_INV: transition false for req={$id} (old={$oldStatus}, new={$newStatus})");
        }

        // Debug in admin_note schreiben (anhängen)
        $stmt = $db->prepare("
        UPDATE shop_requests
        SET admin_note = CONCAT(IFNULL(admin_note,''), '\n', ?)
        WHERE id = ?
    ");
        $stmt->execute([implode(" | ", $dbg), $id]);

        $db->commit();

        // Discord Notification (nach Commit)
        $title = match ($newStatus) {
            'approved'  => '✅ Antrag genehmigt',
            'declined'  => '⛔ Antrag abgelehnt',
            'cancelled' => '🗑️ Antrag storniert',
            default     => '🔔 Antrag Status geändert',
        };

        $color = match ($newStatus) {
            'approved'  => 0x22C55E,
            'declined'  => 0xEF4444,
            'cancelled' => 0x94A3B8,
            default     => 0x3B82F6,
        };

        $buyerName = shop_get_player_name((string)$req['steamid']);
        $adminName = shop_get_player_name($adminSteamId);

        shop_discord_notify($title, [
            'Request ID' => (string)$req['id'],
            'Spieler'    => $buyerName,
            'Item'       => (string)$req['item_name'],
            'Currency'   => (string)$req['currency'],
            'Price'      => (string)$req['price'],
            'Status'     => $oldStatus . ' → ' . $newStatus,
            'Admin'      => $adminName,
            'Note'       => ($note !== '' ? $note : '-'),
        ], $color);
    } catch (Throwable $e) {
        $db->rollBack();
        error_log("SHOP_INV_ERR: " . $e->getMessage());
        throw $e;
    }
}

function shop_discord_webhook_url(): string
{
    // settings.php enthält die Funktion shop_discord_webhook()
    require_once __DIR__ . '/../config/settings.php';

    // Wenn nicht vorhanden: kein Webhook -> skip
    if (!function_exists('shop_discord_webhook')) {
        return '';
    }

    // settings.php wirft aktuell Exception wenn ENV fehlt.
    // Damit dir der Shop nicht crasht, fangen wir das ab und skippen still.
    try {
        $url = shop_discord_webhook();
        return is_string($url) ? $url : '';
    } catch (Throwable $e) {
        error_log('SHOP_WEBHOOK_CFG_ERR: ' . $e->getMessage());
        return '';
    }
}



function shop_discord_notify(string $title, array $fields = [], int $color = 0xE11D48): void
{
    $webhook = shop_discord_webhook_url();
    if ($webhook === '') return; // kein webhook konfiguriert -> silently skip

    $embedFields = [];
    foreach ($fields as $name => $value) {
        if ($value === null) continue;
        $embedFields[] = [
            'name' => (string)$name,
            'value' => (string)$value,
            'inline' => true,
        ];
    }

    $payload = [
        'username' => 'SCUM Shop',
        'embeds' => [[
            'title' => $title,
            'color' => $color,
            'fields' => $embedFields,
            'timestamp' => gmdate('c'),
        ]]
    ];

    $ch = curl_init($webhook);
    curl_setopt_array($ch, [
        CURLOPT_POST => true,
        CURLOPT_HTTPHEADER => ['Content-Type: application/json'],
        CURLOPT_POSTFIELDS => json_encode($payload, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES),
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_CONNECTTIMEOUT => 2,
        CURLOPT_TIMEOUT => 3,
    ]);

    $res = curl_exec($ch);
    $err = curl_error($ch);
    $code = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE);
    curl_close($ch);

    // niemals die URL loggen (Webhook ist ein Secret)
    if ($res === false || $code >= 400) {
        error_log("SHOP_WEBHOOK_ERR: http={$code} curlerr={$err}");
    }
}

function shop_list_requests_for_user(string $steamId, int $limit = 25): array
{
    $stmt = db()->prepare("
      SELECT r.*,
             i.name AS item_name,
             i.requires_coordinates
      FROM shop_requests r
      JOIN shop_items i ON i.id = r.item_id
      WHERE r.steamid = :steamid
        AND r.user_deleted = 0
      ORDER BY r.id DESC
      LIMIT :lim
    ");
    $stmt->bindValue(':steamid', $steamId, PDO::PARAM_STR);
    $stmt->bindValue(':lim', $limit, PDO::PARAM_INT);
    $stmt->execute();
    return $stmt->fetchAll();
}

function shop_cancel_request(int $id, string $steamId): bool
{
    // Vorher laden (für Discord)
    $stmt = db()->prepare("
      SELECT r.id, r.steamid, r.currency, r.price, r.coord_x, r.coord_y,
             i.name AS item_name
      FROM shop_requests r
      JOIN shop_items i ON i.id = r.item_id
      WHERE r.id = :id AND r.steamid = :steamid
      LIMIT 1
    ");
    $stmt->execute([':id' => $id, ':steamid' => $steamId]);
    $row = $stmt->fetch(PDO::FETCH_ASSOC);
    if (!$row) return false;

    // Nur eigene + nur pending
    $stmt = db()->prepare("
      UPDATE shop_requests
      SET status = 'cancelled'
      WHERE id = :id
        AND steamid = :steamid
        AND status = 'pending'
    ");
    $stmt->execute([
        ':id' => $id,
        ':steamid' => $steamId
    ]);

    $ok = ($stmt->rowCount() === 1);

    // Webhook nur wenn wirklich abgebrochen wurde
    if ($ok) {
        $buyerName = function_exists('shop_scum_name_by_steamid')
            ? shop_get_player_name((string)$row['steamid'])
            : (string)$row['steamid'];

        shop_discord_notify('🗑️ Antrag abgebrochen', [
            'Request ID' => (string)$row['id'],
            'Spieler'    => $buyerName,
            'Item'       => (string)$row['item_name'],
            'Currency'   => (string)$row['currency'],
            'Price'      => (string)$row['price'],
            'Coords'     => (!empty($row['coord_x']) || !empty($row['coord_y']))
                ? ((int)$row['coord_x'] . ', ' . (int)$row['coord_y'])
                : '-',
            'Status'     => 'pending → cancelled',
        ], 0x94A3B8);
    }

    return $ok;
}

function shop_user_delete_request(int $id, string $steamId): bool
{
    // Nur eigene + nur cancelled/delivered + nur wenn noch nicht gelöscht
    $stmt = db()->prepare("
      UPDATE shop_requests
      SET user_deleted = 1,
          user_deleted_at = NOW()
      WHERE id = :id
        AND steamid = :steamid
        AND user_deleted = 0
        AND status IN ('cancelled','approved','rejected')
    ");
    $stmt->execute([
        ':id' => $id,
        ':steamid' => $steamId
    ]);

    return $stmt->rowCount() === 1;
}
require_once __DIR__ . '/scum_db.php';

function shop_get_player_name(string $steamId): string
{
    $scum = getScumDbOrNull();
    if (!$scum) return 'Unbekannt';

    try {
        // SCUM.db: Tabelle heißt i.d.R. user_profile, SteamID-Spalte oft "steam_id"
        // Je nach DB-Version kann es auch "steamid" heißen – ich mache beides.
        $stmt = $scum->prepare('SELECT name FROM user_profile WHERE user_id = :sid LIMIT 1');
        if ($stmt) {
            $stmt->bindValue(':sid', $steamId, SQLITE3_TEXT);
            $res = $stmt->execute();
            if ($res) {
                $row = $res->fetchArray(SQLITE3_ASSOC);
                if (!empty($row['name'])) return (string)$row['name'];
            }
        }

        // Fallback, falls Spalte steamid heißt
        $stmt = $scum->prepare('SELECT name FROM user_profile WHERE user_id = :sid LIMIT 1');
        if ($stmt) {
            $stmt->bindValue(':sid', $steamId, SQLITE3_TEXT);
            $res = $stmt->execute();
            if ($res) {
                $row = $res->fetchArray(SQLITE3_ASSOC);
                if (!empty($row['name'])) return (string)$row['name'];
            }
        }
    } catch (Throwable $e) {
        error_log('SHOP_SCUM_NAME_LOOKUP_ERR: ' . $e->getMessage());
    }

    return 'Unbekannt';
}

function shop_buy_with_voucher_instant(string $steamId, int $itemId, int $price, ?int $x, ?int $y): array
{
    $db = db();
    $db->beginTransaction();

    try {
        // Item laden
        $stmt = $db->prepare("
            SELECT id, name, requires_coordinates
            FROM shop_items
            WHERE id = ?
            LIMIT 1
        ");
        $stmt->execute([$itemId]);
        $item = $stmt->fetch(PDO::FETCH_ASSOC);
        if (!$item) {
            $db->rollBack();
            return ['ok' => false, 'msg' => 'Item nicht gefunden.'];
        }

        // Optional: Koordinaten prüfen wenn nötig
        if ((int)($item['requires_coordinates'] ?? 0) === 1) {
            if ($x === null || $y === null) {
                $db->rollBack();
                return ['ok' => false, 'msg' => 'Für dieses Item sind Koordinaten nötig.'];
            }
        }

        // Voucher Bestand locken
        $stmt = $db->prepare("SELECT vouchers FROM user_vouchers WHERE steamid = ? FOR UPDATE");
        $stmt->execute([$steamId]);
        $u = $stmt->fetch(PDO::FETCH_ASSOC);

        $have = (int)($u['vouchers'] ?? 0);
        $need = max(1, $price); // Voucher-Kauf = 1 Gutschein (wenn du pro Item anders willst, sag kurz)

        if ($have < $need) {
            $db->rollBack();
            return ['ok' => false, 'msg' => 'Nicht genügend Gutscheine.'];
        }

        // Voucher abziehen
        $stmt = $db->prepare("UPDATE user_vouchers SET vouchers = vouchers - ? WHERE steamid = ?");
        $stmt->execute([$need, $steamId]);

        // Request trotzdem speichern (für Historie), aber direkt approved
        $stmt = $db->prepare("
            INSERT INTO shop_requests
                (steamid, item_id, currency, price, coord_x, coord_y, status, voucher_charged, admin_note)
            VALUES
                (:steamid, :item_id, 'VOUCHER', :price, :x, :y, 'approved', 1, :note)
        ");
        $stmt->execute([
            ':steamid' => $steamId,
            ':item_id' => $itemId,
            ':price'   => $need,
            ':x'       => $x,
            ':y'       => $y,
            ':note'    => '[AUTO] Voucher-Kauf sofort genehmigt',
        ]);
        $reqId = (int)$db->lastInsertId();

        // Inventar gutschreiben (wenn das ein "Inventory Item" ist)
        // -> Dafür brauchen wir die Info aus shop_items (z.B. is_inventory_item)
        // Falls du die Spalte hast, nimm die. Wenn nicht: wir machen es erstmal "immer ins inventory"
        // und du sagst mir gleich, welche Items NICHT ins inventory sollen.
        $stmt = $db->prepare("
            INSERT INTO user_inventory (steamid, item_name, amount)
            VALUES (:steamid, :item_name, 1)
            ON DUPLICATE KEY UPDATE amount = amount + 1
        ");
        $stmt->execute([
            ':steamid'   => $steamId,
            ':item_name' => (string)$item['name'],
        ]);

        $db->commit();

        shop_discord_notify('⚡ Voucher-Kauf sofort genehmigt', [
            'Request ID' => (string)$reqId,
            'SteamID'    => $steamId,
            'Item'       => (string)$item['name'],
            'Currency'   => 'VOUCHER',
            'Charged'    => (string)$need,
            'Coords'     => ($x !== null && $y !== null) ? ($x . ', ' . $y) : '-',
            'Status'     => 'approved',
        ], 0x8B5CF6);

        return ['ok' => true, 'msg' => 'Kauf erfolgreich (Gutschein abgezogen, Item gutgeschrieben).', 'request_id' => $reqId];
    } catch (Throwable $e) {
        $db->rollBack();
        throw $e;
    }
}
