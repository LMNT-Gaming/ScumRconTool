<?php
declare(strict_types=1);

require_once __DIR__ . '/db_function.php';

function shop_categories_list(): array
{
    $stmt = db()->query("
        SELECT id, slug, name, color, sort_order
        FROM shop_categories
        ORDER BY sort_order ASC, name ASC
    ");
    return $stmt->fetchAll() ?: [];
}

function shop_category_create(array $data): array
{
    $name = trim((string)($data['name'] ?? ''));
    $slug = trim((string)($data['slug'] ?? ''));
    $color = trim((string)($data['color'] ?? 'rgba(255,255,255,0.35)'));
    $sort = (int)($data['sort_order'] ?? 0);

    // slug normalisieren
    $slug = strtolower($slug);
    $slug = preg_replace('/[^a-z0-9\-]/', '-', $slug);
    $slug = preg_replace('/-+/', '-', $slug);
    $slug = trim($slug, '-');

    if ($name === '' || $slug === '') {
        return ['ok' => false, 'msg' => 'Name und Slug sind Pflicht.'];
    }

    try {
        $st = db()->prepare("
            INSERT INTO shop_categories (slug, name, color, sort_order)
            VALUES (:slug,:name,:color,:sort)
        ");
        $st->execute([
            ':slug' => $slug,
            ':name' => $name,
            ':color' => $color,
            ':sort' => $sort
        ]);
        return ['ok' => true, 'msg' => 'Kategorie erstellt.'];
    } catch (Throwable $e) {
        return ['ok' => false, 'msg' => 'Kategorie existiert bereits oder DB-Fehler.'];
    }
}

function shop_category_delete(int $id): array
{
    // Schutz-Check: hängen Items dran?
    $st = db()->prepare("SELECT COUNT(*) FROM shop_items WHERE category_id = ?");
    $st->execute([$id]);
    $cnt = (int)$st->fetchColumn();

    if ($cnt > 0) {
        return ['ok' => false, 'msg' => "Kategorie kann nicht gelöscht werden – $cnt Items hängen daran."];
    }

    $del = db()->prepare("DELETE FROM shop_categories WHERE id = ?");
    $del->execute([$id]);

    return ['ok' => true, 'msg' => 'Kategorie gelöscht.'];
}
function shop_category_update(int $id, array $data): array
{
    $name  = trim((string)($data['name'] ?? ''));
    $slug  = trim((string)($data['slug'] ?? ''));
    $color = trim((string)($data['color'] ?? 'rgba(255,255,255,0.35)'));
    $sort  = (int)($data['sort_order'] ?? 0);

    // slug normalisieren
    $slug = strtolower($slug);
    $slug = preg_replace('/[^a-z0-9\-]/', '-', $slug);
    $slug = preg_replace('/-+/', '-', $slug);
    $slug = trim($slug, '-');

    if ($id <= 0 || $name === '' || $slug === '') {
        return ['ok' => false, 'msg' => 'Ungültige Daten.'];
    }

    try {
        $st = db()->prepare("
            UPDATE shop_categories
            SET slug = :slug, name = :name, color = :color, sort_order = :sort
            WHERE id = :id
        ");
        $st->execute([
            ':id' => $id,
            ':slug' => $slug,
            ':name' => $name,
            ':color' => $color,
            ':sort' => $sort
        ]);

        return ['ok' => true, 'msg' => 'Kategorie gespeichert.'];
    } catch (Throwable $e) {
        return ['ok' => false, 'msg' => 'Slug bereits vergeben oder DB-Fehler.'];
    }
}
