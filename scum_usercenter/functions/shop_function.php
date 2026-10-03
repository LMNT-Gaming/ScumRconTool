<?php
//shop_function.php
declare(strict_types=1);

require_once __DIR__ . '/db_function.php';

function shop_create_item(array $data): int
{
    db()->beginTransaction();

    $stmt = db()->prepare("
  INSERT INTO shop_items
    (name, description, image, category_id, is_inventory_item, is_active, requires_coordinates)
  VALUES
    (:name,:description,:image,:category_id,:is_inventory_item,:is_active,:requires_coordinates)
");
    $stmt->execute([
        ':name' => trim((string)$data['name']),
        ':description' => trim((string)($data['description'] ?? '')),
        ':image' => trim((string)($data['image'] ?? '')),
        ':category_id' => !empty($data['category_id']) ? (int)$data['category_id'] : null,
        ':is_inventory_item' => !empty($data['is_inventory_item']) ? 1 : 0,
        ':is_active' => !empty($data['is_active']) ? 1 : 0,
        ':requires_coordinates' => !empty($data['requires_coordinates']) ? 1 : 0,
    ]);



    $itemId = (int)db()->lastInsertId();
    shop_save_prices($itemId, $data);

    db()->commit();
    return $itemId;
}

function shop_save_prices(int $itemId, array $data): void
{
    // erwartete Inputs: price_scum_dollar, price_gold, price_voucher (leer oder Zahl)
    $map = [
        'SCUM_DOLLAR' => $data['price_scum_dollar'] ?? '',
        'GOLD'        => $data['price_gold'] ?? '',
        'VOUCHER'     => $data['price_voucher'] ?? '',
    ];

    // erst alle Preise löschen, dann neu setzen (simple & sicher)
    $del = db()->prepare("DELETE FROM shop_item_prices WHERE item_id = ?");
    $del->execute([$itemId]);

    $ins = db()->prepare("INSERT INTO shop_item_prices (item_id, currency, price) VALUES (?,?,?)");

    foreach ($map as $currency => $val) {
        $val = trim((string)$val);
        if ($val === '') continue;

        $price = (int)$val;
        if ($price <= 0) continue;

        $ins->execute([$itemId, $currency, $price]);
    }
}


function shop_list_items(): array
{
    $sql = "
      SELECT 
        i.*,
        c.name  AS category_name,
        c.slug  AS category_slug,
        c.color AS category_color,
        GROUP_CONCAT(CONCAT(p.currency, ':', p.price) ORDER BY p.currency SEPARATOR ',') AS prices_kv
      FROM shop_items i
      LEFT JOIN shop_categories c ON c.id = i.category_id
      LEFT JOIN shop_item_prices p ON p.item_id = i.id
      GROUP BY i.id
      ORDER BY i.category_id, i.id DESC
    ";

    $stmt = db()->query($sql);
    $rows = $stmt->fetchAll();

    foreach ($rows as &$r) {
        $r['prices'] = [];
        if (!empty($r['prices_kv'])) {
            $pairs = explode(',', $r['prices_kv']);
            foreach ($pairs as $pair) {
                $parts = explode(':', $pair, 2);
                if (count($parts) === 2) {
                    $cur = $parts[0];
                    $val = (int)$parts[1];
                    $r['prices'][$cur] = $val;
                }
            }
        }

        $pretty = [];
        if (!empty($r['prices']['SCUM_DOLLAR'])) $pretty[] = 'SCUM$: ' . (int)$r['prices']['SCUM_DOLLAR'];
        if (!empty($r['prices']['GOLD']))       $pretty[] = 'GOLD: ' . (int)$r['prices']['GOLD'];
        if (!empty($r['prices']['VOUCHER']))    $pretty[] = 'VOUCHER: ' . (int)$r['prices']['VOUCHER'];
        $r['prices_text'] = $pretty ? implode(' | ', $pretty) : '—';
    }
    unset($r);

    return $rows;
}


function shop_get_item(int $id): ?array
{
    $stmt = db()->prepare("
  SELECT i.*, c.name AS category_name, c.slug AS category_slug, c.color AS category_color
  FROM shop_items i
  LEFT JOIN shop_categories c ON c.id = i.category_id
  WHERE i.id = ?
");

    $stmt->execute([$id]);
    $item = $stmt->fetch();
    if (!$item) return null;

    $p = db()->prepare("SELECT currency, price FROM shop_item_prices WHERE item_id = ?");
    $p->execute([$id]);
    $prices = $p->fetchAll();

    $item['prices'] = [];
    foreach ($prices as $row) {
        $item['prices'][$row['currency']] = (int)$row['price'];
    }

    return $item;
}
function shop_get_prices_for_item(int $itemId): array
{
    $stmt = db()->prepare("SELECT currency, price FROM shop_item_prices WHERE item_id = ?");
    $stmt->execute([$itemId]);
    $rows = $stmt->fetchAll();

    $out = [];
    foreach ($rows as $r) {
        $out[$r['currency']] = (int)$r['price'];
    }
    return $out;
}


function shop_update_item(int $id, array $data): void
{
    db()->beginTransaction();

    $stmt = db()->prepare("
  UPDATE shop_items SET
    name=:name,
    description=:description,
    image=:image,
    category_id=:category_id,
    is_inventory_item=:is_inventory_item,
    is_active=:is_active,
    requires_coordinates=:requires_coordinates
  WHERE id=:id
");

    $stmt->execute([
        ':id' => $id,
        ':name' => trim((string)$data['name']),
        ':description' => trim((string)($data['description'] ?? '')),
        ':image' => trim((string)($data['image'] ?? '')),
        ':category_id' => !empty($data['category_id']) ? (int)$data['category_id'] : null,
        ':is_inventory_item' => !empty($data['is_inventory_item']) ? 1 : 0,
        ':is_active' => !empty($data['is_active']) ? 1 : 0,
        ':requires_coordinates' => !empty($data['requires_coordinates']) ? 1 : 0,
    ]);



    shop_save_prices($id, $data);

    db()->commit();
}


function shop_delete_item(int $id): void
{
    $stmt = db()->prepare("DELETE FROM shop_items WHERE id = ?");
    $stmt->execute([$id]);
}
